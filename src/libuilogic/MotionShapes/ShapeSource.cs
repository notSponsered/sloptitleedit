using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.UiLogic.Assa;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>Decides whether a selected line is a drawing Motion shapes may replace (it becomes the first key) or is left alone.</summary>
public static class ShapeSource
{
    // anything that moves, warps or clips the drawing - such a line would lose its effect when replaced
    private static readonly Regex EffectRegex = new(@"\\(?:move|org|fr[xyz]?[-\d.]|fsc[xy]|fa[xy]|t\s*\(|i?clip)", RegexOptions.Compiled);

    /// <summary>
    /// The line's shape in script px when it is a single-path, top-left aligned drawing placed with \pos (or not at all) and
    /// nothing that animates or warps it; otherwise null (new lines are inserted and the line is kept).
    /// </summary>
    /// <param name="marginLeft">The line's own margin (0 = the style's), as libass uses it.</param>
    public static PenPath? GetReplaceablePath(string text, string styleName, string header, int marginLeft = 0, int marginVertical = 0)
    {
        var drawing = AssaDrawingText.Parse(text);
        if (drawing == null || EffectRegex.IsMatch(text))
        {
            return null;
        }

        var style = AdvancedSubStationAlpha.GetSsaStyle(styleName, header);
        var alignment = AssaTags.FirstAlignment(text) ?? (int.TryParse(style.Alignment, out var a) ? a : 2);
        if (alignment != 7 || style.ScaleX != 100 || style.ScaleY != 100 || style.Angle != 0 ||
            (!text.Contains("\\pos", StringComparison.Ordinal) && ((marginLeft != 0 ? marginLeft : style.MarginLeft) != 0 || (marginVertical != 0 ? marginVertical : style.MarginVertical) != 0))) // no \pos: drawn at the margins
        {
            return null;
        }

        var paths = drawing.ToPaths();
        return paths is [{ Anchors.Count: >= 3 } path] ? path : null;
    }
}
