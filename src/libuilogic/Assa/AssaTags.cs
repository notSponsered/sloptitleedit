using System.Globalization;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.Assa;

/// <summary>
/// Small helpers for reading/writing ASSA override tags in a line's text.
/// </summary>
public static class AssaTags
{
    private static readonly Regex FadRegex = new(@"\\fad\s*\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\)", RegexOptions.Compiled);
    private static readonly Regex AnyFadeRegex = new(@"\\fade?\s*\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex AlignmentRegex = new(@"\\an([1-9])", RegexOptions.Compiled);

    /// <summary>
    /// Returns the values of the first \fad(in,out) tag, or null if there is none.
    /// </summary>
    public static (int In, int Out)? GetFade(string text)
    {
        var match = FadRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        return ((int)Math.Round(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)),
                (int)Math.Round(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Removes all \fad/\fade tags and inserts a single \fad(in,out) into the first override block.
    /// </summary>
    public static string SetFade(string text, int fadeIn, int fadeOut)
    {
        var s = AnyFadeRegex.Replace(text, string.Empty).Replace("{}", string.Empty);
        return InsertIntoFirstBlock(s, $"\\fad({fadeIn},{fadeOut})");
    }

    /// <summary>
    /// Inserts a tag (e.g. "\pos(1,2)") at the start of the leading override block, or prepends a new block.
    /// </summary>
    public static string InsertIntoFirstBlock(string text, string tag)
    {
        if (text.StartsWith("{\\", StringComparison.Ordinal))
        {
            return text.Insert(1, tag);
        }

        return "{" + tag + "}" + text;
    }

    /// <summary>
    /// Returns the first inline \anN alignment, or null.
    /// </summary>
    public static int? FirstAlignment(string text)
    {
        var match = AlignmentRegex.Match(text);
        return match.Success ? match.Groups[1].Value[0] - '0' : null;
    }
}
