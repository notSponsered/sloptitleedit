using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using System.Text.RegularExpressions;
using static Nikse.SubtitleEdit.UiLogic.MotionTracking.MotionApplier;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>Frames <see cref="First"/>..<see cref="Last"/> (positions in the frame list) written as one line.</summary>
public readonly record struct ShapeSegment(int First, int Last);

/// <summary>
/// Turns per-frame shapes into as few ASSA lines as possible: a still shape is one \pos line, a shape sliding in a straight line
/// is one \move line, anything else (morphing, rotating, zooming) is one line per frame.
/// Coordinates are absolute ({\an7\pos(0,0)\p1} + the drawing in script px), so there is no pivot or alignment question.
/// </summary>
public static class ShapeLines
{
    /// <summary>Video px a point may be off the straight-line prediction and still share a line.</summary>
    public const double Tolerance = 0.5; // ponytail: fixed; expose if line counts annoy

    private const int ColorTolerance = 3;

    /// <summary>Script px the written outline may differ from the computed one, for fewer drawing points (0 = exact).</summary>
    public const double DefaultSimplify = 1.0;

    private static readonly Regex DroppedTagsRegex = new(
        @"\\(?:t\s*\((?:[^()]|\([^()]*\))*\)|i?clip\s*\([^)]*\)|pos\s*\([^)]*\)|move\s*\([^)]*\)|org\s*\([^)]*\)|an\d|p-?\d+|fsc[xy][-\d.]*|fr[xyz]?[-\d.]*)",
        RegexOptions.Compiled);

    private static readonly Regex ColorRegex = new(@"\\1?c&H[0-9a-fA-F]*&?", RegexOptions.Compiled);

    public static List<ShapeSegment> Segment(IReadOnlyList<ShapeFrame> frames, IReadOnlyList<double> timesMs, bool useColor)
    {
        var result = new List<ShapeSegment>();
        var i = 0;
        while (i < frames.Count)
        {
            var j = i;
            while (j + 1 < frames.Count && Fits(frames, timesMs, i, j + 1, useColor))
            {
                j++;
            }

            result.Add(new ShapeSegment(i, j));
            i = j + 1;
        }

        return result;
    }

    /// <summary>Can frames i..j be one line: same topology, and every frame where moving i's shape in a straight line puts it?</summary>
    private static bool Fits(IReadOnlyList<ShapeFrame> frames, IReadOnlyList<double> timesMs, int i, int j, bool useColor)
    {
        // ponytail: O(run² · points) - a 900-frame hold of a 256-point polygon is ~0.3 s; binary-search the run end if it lags
        var first = frames[i].Path;
        var last = frames[j].Path;
        if (!SameTopology(first, last))
        {
            return false;
        }

        var (cix, ciy) = Mean(first);
        var (cjx, cjy) = Mean(last);
        for (var f = i + 1; f <= j; f++)
        {
            var path = frames[f].Path;
            if (!SameTopology(first, path))
            {
                return false;
            }

            var u = timesMs[j] > timesMs[i] ? (timesMs[f] - timesMs[i]) / (timesMs[j] - timesMs[i]) : 1;
            var dx = u * (cjx - cix);
            var dy = u * (cjy - ciy);
            for (var k = 0; k < first.Anchors.Count; k++)
            {
                var p = first.Anchors[k];
                var q = path.Anchors[k];
                if (Off(p.X + dx, p.Y + dy, q.X, q.Y) || Off(p.InX + dx, p.InY + dy, q.InX, q.InY) || Off(p.OutX + dx, p.OutY + dy, q.OutX, q.OutY))
                {
                    return false;
                }
            }

            if (useColor && frames[i].Color is { } ci && frames[j].Color is { } cj && frames[f].Color is { } cf &&
                (Off(ci.Red, cj.Red, cf.Red, u) || Off(ci.Green, cj.Green, cf.Green, u) || Off(ci.Blue, cj.Blue, cf.Blue, u)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Off(double px, double py, double qx, double qy) => (px - qx) * (px - qx) + (py - qy) * (py - qy) > Tolerance * Tolerance;

    private static bool Off(byte a, byte b, byte actual, double u) => Math.Abs(a + (b - a) * u - actual) > ColorTolerance;

    private static bool SameTopology(PenPath a, PenPath b) => a.Anchors.Count == b.Anchors.Count && a.HasCurves == b.HasCurves;

    private static (double X, double Y) Mean(PenPath path) => (path.Anchors.Average(a => a.X), path.Anchors.Average(a => a.Y));

    /// <summary>
    /// The lines for the segments, covering <paramref name="spanStartMs"/>..<paramref name="spanEndMs"/>. Piece boundaries sit
    /// midway between frames, rounded to centiseconds (as Motion tracking does). <paramref name="userTags"/> (from
    /// <see cref="KeepTags"/>) go into every line; a \fad in them continues across the pieces.
    /// </summary>
    public static List<MotionPiece> Write(IReadOnlyList<ShapeFrame> frames, IReadOnlyList<double> timesMs, IReadOnlyList<ShapeSegment> segments,
        double spanStartMs, double spanEndMs, string header, string styleName, int videoWidth, int videoHeight, string userTags, bool useColor, double simplify = DefaultSimplify)
    {
        var (sx, sy) = ScriptScale(header, videoWidth, videoHeight);
        var style = AdvancedSubStationAlpha.GetSsaStyle(styleName, header);
        var neutral = style.ScaleX == 100 && style.ScaleY == 100 && style.Angle == 0 ? string.Empty : "\\fscx100\\fscy100\\frz0";
        var duration = spanEndMs - spanStartMs;
        var pieces = new List<MotionPiece>();
        foreach (var segment in segments)
        {
            var start = segment.First == 0 ? spanStartMs : RoundToCentiseconds((timesMs[segment.First - 1] + timesMs[segment.First]) / 2);
            var end = segment.Last == frames.Count - 1 ? spanEndMs : RoundToCentiseconds((timesMs[segment.Last] + timesMs[segment.Last + 1]) / 2);
            if (end <= start)
            {
                continue;
            }

            var first = frames[segment.First];
            var last = frames[segment.Last];
            var (cix, ciy) = Mean(first.Path);
            var (cjx, cjy) = Mean(last.Path);
            var dx = (cjx - cix) * sx;
            var dy = (cjy - ciy) * sy;
            var t1 = Math.Max(0, timesMs[segment.First] - start);
            var t2 = Math.Max(0, timesMs[segment.Last] - start);
            var placement = Math.Abs(dx) < 0.005 && Math.Abs(dy) < 0.005
                ? "\\pos(0,0)"
                : $"\\move(0,0,{F(dx)},{F(dy)},{I(t1)},{I(t2)})";

            var color = string.Empty;
            if (useColor && first.Color is { } c1)
            {
                color = "\\1c" + AdvancedSubStationAlpha.GetSsaColorStringNoTransparency(c1) + "&";
                if (last.Color is { } c2 && c2 != c1)
                {
                    color += $"\\t({I(t1)},{I(t2)},\\1c{AdvancedSubStationAlpha.GetSsaColorStringNoTransparency(c2)}&)";
                }
            }

            var tags = ShiftFades(userTags, start - spanStartMs, end - start, duration);
            var path = ShapeSimplify.Simplify(first.Path, simplify / Math.Max(sx, sy)); // script px -> video px

            var drawing = new AssaDrawingText("{\\an7" + placement + neutral + tags + color + "\\p1}", string.Empty, "{\\p0}", 1, 0, 0);
            pieces.Add(new MotionPiece(start, end, drawing.WithPaths([Scale(path, sx, sy)])));
        }

        return pieces;
    }

    /// <summary>Script px per video px; a missing PlayRes is 384x288, as in libass. Use it both ways so a shape keeps its place.</summary>
    public static (double X, double Y) ScriptScale(string header, int videoWidth, int videoHeight) =>
        (GetPlayRes(header, "PlayResX", AdvancedSubStationAlpha.DefaultWidth) / videoWidth,
         GetPlayRes(header, "PlayResY", AdvancedSubStationAlpha.DefaultHeight) / videoHeight);

    public static PenPath Scale(PenPath path, double sx, double sy)
    {
        var result = new PenPath();
        result.Anchors.AddRange(path.Anchors.Select(a => new PenAnchor(a.X * sx, a.Y * sy)
        {
            InX = a.InX * sx,
            InY = a.InY * sy,
            OutX = a.OutX * sx,
            OutY = a.OutY * sy,
        }));
        return result;
    }

    /// <summary>
    /// The tags of the source line's first override block that still make sense on a moving shape (\blur, \bord, alpha, \3c, \fad...),
    /// without the braces. Placement, drawing mode, scale/rotation, clips and animations are dropped, and the fill colour when
    /// colour is tracked.
    /// </summary>
    public static string KeepTags(string text, bool dropColor)
    {
        if (string.IsNullOrEmpty(text) || !text.StartsWith('{'))
        {
            return string.Empty;
        }

        var end = text.IndexOf('}');
        if (end < 0)
        {
            return string.Empty;
        }

        var tags = DroppedTagsRegex.Replace(text[1..end], string.Empty);
        return dropColor ? ColorRegex.Replace(tags, string.Empty) : tags;
    }
}
