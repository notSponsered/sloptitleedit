using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.Assa;

/// <summary>
/// An ASSA drawing line's text split around its drawing commands, so editing replaces only the commands and keeps
/// every tag (\pos, \fad, \an7, \t...), e.g. "{\an7\pos(100,50)\p1}m 0 0 l 10 0 10 10{\p0}".
/// Paths are in script coordinates: \pos is the origin (top-left aligned drawings) and \pN scaling is applied.
/// </summary>
public sealed record AssaDrawingText(string Prefix, string Commands, string Suffix, int Scale, double OriginX, double OriginY)
{
    private static readonly Regex DrawingBlockRegex = new(@"\{[^}]*\\p([1-9]\d*)[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex PosRegex = new(@"\\pos\s*\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)", RegexOptions.Compiled);

    /// <summary>Template for a new drawing line.</summary>
    public static AssaDrawingText NewLine { get; } = new("{\\p1}", string.Empty, "{\\p0}", 1, 0, 0);

    public static AssaDrawingText? Parse(string text)
    {
        var match = DrawingBlockRegex.Match(text);
        if (!match.Success)
        {
            return null;
        }

        var start = match.Index + match.Length;
        var end = text.IndexOf('{', start);
        if (end < 0)
        {
            end = text.Length;
        }

        var pos = PosRegex.Match(text);
        var originX = pos.Success ? Number(pos.Groups[1].Value) : 0;
        var originY = pos.Success ? Number(pos.Groups[2].Value) : 0;
        var scale = int.TryParse(match.Groups[1].Value, out var s) ? s : 1;
        return new AssaDrawingText(text[..start], text[start..end], text[end..], scale, originX, originY);
    }

    private double Factor => Math.Pow(2, Scale - 1);

    /// <summary>The shapes ("m" starts each), or null when the drawing uses spline commands (s/p/c) a pen path can't edit.</summary>
    public List<PenPath>? ToPaths()
    {
        var paths = new List<List<PenPoint>>();
        List<PenPoint>? current = null;
        var mode = ' ';
        var curveIndex = 0;
        var tokens = Commands.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                switch (char.ToLowerInvariant(token[0]))
                {
                    case 'm':
                    case 'n':
                        current = [];
                        paths.Add(current);
                        mode = 'm';
                        break;
                    case 'l':
                        mode = 'l';
                        break;
                    case 'b':
                        mode = 'b';
                        curveIndex = 0;
                        break;
                    default:
                        return null; // s, p, c (b-splines)
                }

                continue;
            }

            if (current == null || i + 1 >= tokens.Length ||
                !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                continue;
            }

            i++;
            x = x / Factor + OriginX;
            y = y / Factor + OriginY;
            switch (mode)
            {
                case 'm':
                    current.Add(new PenPoint(PenPointKind.Start, x, y));
                    mode = 'l'; // more pairs after "m x y" are lines
                    break;
                case 'b':
                    var kind = (curveIndex++ % 3) switch { 0 => PenPointKind.Control1, 1 => PenPointKind.Control2, _ => PenPointKind.CurveEnd };
                    current.Add(new PenPoint(kind, x, y));
                    break;
                default:
                    current.Add(new PenPoint(PenPointKind.Line, x, y));
                    break;
            }
        }

        return paths.Where(p => p.Count > 0).Select(PenPath.FromPoints).ToList();
    }

    /// <summary>The line text with its drawing replaced by these paths (script coordinates).</summary>
    public string WithPaths(IEnumerable<PenPath> paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            foreach (var p in path.ToPoints())
            {
                var command = p.Kind switch
                {
                    PenPointKind.Start => "m ",
                    PenPointKind.Line => "l ",
                    PenPointKind.Control1 => "b ",
                    _ => string.Empty,
                };
                sb.Append(command);
                sb.Append(Format((p.X - OriginX) * Factor)).Append(' ');
                sb.Append(Format((p.Y - OriginY) * Factor)).Append(' ');
            }
        }

        return Prefix + sb.ToString().TrimEnd() + Suffix;
    }

    private static double Number(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string Format(double v) => (Math.Round(v, 2) + 0.0).ToString("0.##", CultureInfo.InvariantCulture);
}
