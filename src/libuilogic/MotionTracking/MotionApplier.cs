using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.UiLogic.Assa;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.MotionTracking;

/// <summary>
/// Tracked state of one video frame: time in seconds, tracked point in video pixels,
/// scale and rotation (degrees, clockwise on screen) relative to where tracking started.
/// </summary>
public readonly record struct MotionSample(double Time, double X, double Y, double Scale, double Rotation, double Score);

/// <summary>
/// A recorded motion, kept for the session so it can be applied to more lines later.
/// </summary>
public sealed class MotionTrack
{
    public required string VideoFileName { get; init; }

    /// <summary>Sorted by time.</summary>
    public List<MotionSample> Samples { get; init; } = [];
}

public readonly record struct MotionPiece(double StartMs, double EndMs, string Text);

/// <summary>
/// Splits an ASSA line into one event per frame that follows a recorded motion
/// (per-frame \pos, plus \fscx/\fscy/\frz when the track has scale/rotation).
/// </summary>
public static class MotionApplier
{
    private static readonly Regex PosOrMoveRegex = new(@"\\(pos|move)\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex OrgRegex = new(@"\\org\s*\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex RectClipRegex = new(@"\\(i?clip)\s*\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex VectorClipRegex = new(@"\\(i?clip)\s*\(\s*(?:(\d+)\s*,)?\s*([mnlbspc][^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex FadRegex = new(@"\\fad\s*\(\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex FadeRegex = new(@"\\fade\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex ScaleRegex = new(@"\\fsc([xy])([-\d.]+)", RegexOptions.Compiled);
    private static readonly Regex RotationRegex = new(@"\\frz?([-\d.]+)", RegexOptions.Compiled);
    private static readonly Regex TransformRegex = new(@"\\t\s*\((?:[^()]|\([^()]*\))*\)", RegexOptions.Compiled);
    private static readonly Regex KaraokeRegex = new(@"\\(k|K|kf|ko)\d", RegexOptions.Compiled);

    public static bool HasKaraoke(string text) => KaraokeRegex.IsMatch(text);

    /// <summary>
    /// Returns the pieces replacing the line, or null when no tracked frame falls inside the line.
    /// The line is assumed to be placed correctly on the frame closest to <paramref name="referenceTime"/> (seconds).
    /// </summary>
    public static List<MotionPiece>? Apply(string text, string styleName, double startMs, double endMs, string header,
        int videoWidth, int videoHeight, IReadOnlyList<MotionSample> samples, double referenceTime)
    {
        var frames = samples.Where(s => s.Time * 1000.0 >= startMs && s.Time * 1000.0 < endMs).ToList();
        if (frames.Count == 0 || videoWidth <= 0 || videoHeight <= 0)
        {
            return null;
        }

        var reference = samples.MinBy(s => Math.Abs(s.Time - referenceTime));
        var style = AdvancedSubStationAlpha.GetSsaStyle(styleName, header);
        var playResX = GetPlayRes(header, "PlayResX", AdvancedSubStationAlpha.DefaultWidth);
        var playResY = GetPlayRes(header, "PlayResY", AdvancedSubStationAlpha.DefaultHeight);
        var toScriptX = playResX / videoWidth;
        var toScriptY = playResY / videoHeight;
        var duration = endMs - startMs;
        var anchorAt = MakeAnchor(text, style, playResX, playResY, duration);

        // State per frame: position in script coordinates, anchor (for translating other coordinates), scale, rotation
        var states = frames.Select(f =>
        {
            var tRel = f.Time * 1000.0 - startMs;
            var (ax, ay) = anchorAt(tRel);
            var scale = reference.Scale > 0 ? f.Scale / reference.Scale : 1;
            var rotation = f.Rotation - reference.Rotation;
            var radians = rotation * Math.PI / 180.0;

            // video pixels: pos = P + s * R * (A - Pref)
            var vx = ax / toScriptX - reference.X;
            var vy = ay / toScriptY - reference.Y;
            var px = f.X + scale * (Math.Cos(radians) * vx - Math.Sin(radians) * vy);
            var py = f.Y + scale * (Math.Sin(radians) * vx + Math.Cos(radians) * vy);
            return new FrameState(f.Time * 1000.0, Math.Round(px * toScriptX, 2), Math.Round(py * toScriptY, 2), ax, ay,
                Math.Round(scale, 4), Math.Round(rotation, 2));
        }).ToList();

        // Piece boundaries at the midpoint between frames, rounded to centiseconds (ASS precision)
        var pieces = new List<MotionPiece>();
        var groupStart = 0;
        for (var i = 1; i <= states.Count; i++)
        {
            if (i < states.Count && states[i].SameAs(states[groupStart]))
            {
                continue;
            }

            var pieceStart = groupStart == 0 ? startMs : RoundToCentiseconds((states[groupStart - 1].TimeMs + states[groupStart].TimeMs) / 2.0);
            var pieceEnd = i == states.Count ? endMs : RoundToCentiseconds((states[i - 1].TimeMs + states[i].TimeMs) / 2.0);
            if (pieceEnd > pieceStart)
            {
                pieces.Add(new MotionPiece(pieceStart, pieceEnd,
                    BuildText(text, states[groupStart], style, pieceStart - startMs, pieceEnd - pieceStart, duration)));
            }

            groupStart = i;
        }

        return pieces;
    }

    private sealed record FrameState(double TimeMs, double X, double Y, double AnchorX, double AnchorY, double Scale, double Rotation)
    {
        // differences this small are invisible - don't split the line for them
        public bool SameAs(FrameState other) =>
            Math.Abs(X - other.X) < 0.05 && Math.Abs(Y - other.Y) < 0.05 &&
            Math.Abs(Scale - other.Scale) < 0.0005 && Math.Abs(Rotation - other.Rotation) < 0.02;
    }

    private static double RoundToCentiseconds(double ms) => Math.Round(ms / 10.0, MidpointRounding.AwayFromZero) * 10.0;

    private static double GetPlayRes(string header, string tag, int fallback)
    {
        var value = AdvancedSubStationAlpha.GetTagValueFromHeader(tag, "[Script Info]", header ?? string.Empty);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;
    }

    /// <summary>
    /// Anchor (script coordinates) as a function of time since line start: first \pos, else \move, else from alignment + style margins.
    /// </summary>
    private static Func<double, (double X, double Y)> MakeAnchor(string text, SsaStyle style, double playResX, double playResY, double duration)
    {
        var match = PosOrMoveRegex.Match(text);
        if (match.Success)
        {
            var args = match.Groups[2].Value.Split(',').Select(ParseNumber).ToArray();
            if (match.Groups[1].Value == "pos" && args.Length == 2)
            {
                return _ => (args[0], args[1]);
            }

            if (match.Groups[1].Value == "move" && args.Length is 4 or 6)
            {
                var t1 = args.Length == 6 ? args[4] : 0;
                var t2 = args.Length == 6 ? args[5] : 0;
                if (t1 == 0 && t2 == 0)
                {
                    t2 = duration;
                }

                return t =>
                {
                    var k = t <= t1 ? 0 : t >= t2 ? 1 : (t - t1) / (t2 - t1);
                    return (args[0] + (args[2] - args[0]) * k, args[1] + (args[3] - args[1]) * k);
                };
            }
        }

        var alignment = AssaTags.FirstAlignment(text) ?? (int.TryParse(style.Alignment, out var a) && a is >= 1 and <= 9 ? a : 2);
        var x = (alignment % 3) switch
        {
            1 => style.MarginLeft,
            2 => (playResX + style.MarginLeft - style.MarginRight) / 2.0,
            _ => playResX - style.MarginRight,
        };
        var y = alignment <= 3 ? playResY - style.MarginVertical : alignment <= 6 ? playResY / 2.0 : style.MarginVertical;
        return _ => (x, y);
    }

    private static string BuildText(string text, FrameState state, SsaStyle style, double offsetMs, double pieceMs, double durationMs)
    {
        var dx = state.X - state.AnchorX;
        var dy = state.Y - state.AnchorY;

        var s = PosOrMoveRegex.Replace(text, string.Empty);

        // other screen coordinates follow the anchor
        s = OrgRegex.Replace(s, m => $"\\org({F(ParseNumber(m.Groups[1].Value) + dx)},{F(ParseNumber(m.Groups[2].Value) + dy)})");
        s = RectClipRegex.Replace(s, m =>
            $"\\{m.Groups[1].Value}({F(ParseNumber(m.Groups[2].Value) + dx)},{F(ParseNumber(m.Groups[3].Value) + dy)},{F(ParseNumber(m.Groups[4].Value) + dx)},{F(ParseNumber(m.Groups[5].Value) + dy)})");
        s = VectorClipRegex.Replace(s, m =>
        {
            var clipScale = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 1;
            var factor = Math.Pow(2, clipScale - 1);
            var prefix = m.Groups[2].Success ? m.Groups[2].Value + "," : string.Empty;
            return $"\\{m.Groups[1].Value}({prefix}{TranslateDrawing(m.Groups[3].Value, dx * factor, dy * factor)})";
        });

        // animations are relative to event start - shift them so each piece continues where the previous one was
        var isWholeLine = offsetMs == 0 && pieceMs >= durationMs;
        if (!isWholeLine)
        {
            s = ShiftTransforms(s, offsetMs, durationMs);
            s = FadeRegex.Replace(s, m =>
            {
                var args = m.Groups[1].Value.Split(',');
                if (args.Length != 7)
                {
                    return m.Value;
                }

                return $"\\fade({args[0].Trim()},{args[1].Trim()},{args[2].Trim()},{string.Join(",", args.Skip(3).Select(a => I(ParseNumber(a) - offsetMs)))})";
            });
        }

        s = FadRegex.Replace(s, m =>
        {
            var fadeIn = ParseNumber(m.Groups[1].Value);
            var fadeOut = ParseNumber(m.Groups[2].Value);
            var overlapsFade = offsetMs < fadeIn || offsetMs + pieceMs > durationMs - fadeOut;
            if (!overlapsFade)
            {
                return string.Empty; // fully opaque part of the line
            }

            if (isWholeLine)
            {
                return m.Value;
            }

            return $"\\fade(255,0,255,{I(-offsetMs)},{I(fadeIn - offsetMs)},{I(durationMs - fadeOut - offsetMs)},{I(durationMs - offsetMs)})";
        });

        // scale + rotation from the track: every explicit tag is adjusted, and the style's value is set up front for
        // what the leading block doesn't set itself (e.g. a line with only \fscx, or a tag that only comes mid-line)
        var lead = LeadingTags(s);
        if (Math.Abs(state.Scale - 1) >= 0.0005)
        {
            s = ScaleRegex.Replace(s, m => $"\\fsc{m.Groups[1].Value}{F(ParseNumber(m.Groups[2].Value) * state.Scale)}");
            var missing = (lead.Contains("\\fscx", StringComparison.Ordinal) ? string.Empty : $"\\fscx{F((double)style.ScaleX * state.Scale)}") +
                          (lead.Contains("\\fscy", StringComparison.Ordinal) ? string.Empty : $"\\fscy{F((double)style.ScaleY * state.Scale)}");
            if (missing.Length > 0)
            {
                s = AssaTags.InsertIntoFirstBlock(s, missing);
            }
        }

        if (Math.Abs(state.Rotation) >= 0.005)
        {
            // track rotation is clockwise on screen, \frz is counter-clockwise
            s = RotationRegex.Replace(s, m => $"\\frz{F(ParseNumber(m.Groups[1].Value) - state.Rotation)}");
            if (!RotationRegex.IsMatch(lead))
            {
                s = AssaTags.InsertIntoFirstBlock(s, $"\\frz{F((double)style.Angle - state.Rotation)}");
            }
        }

        s = s.Replace("{}", string.Empty);
        return AssaTags.InsertIntoFirstBlock(s, $"\\pos({F(state.X)},{F(state.Y)})");
    }

    /// <summary>
    /// Shifts \t(...) times by -offset. Whole-event forms \t(tags) and \t(accel,tags) get explicit times first.
    /// </summary>
    private static string ShiftTransforms(string s, double offsetMs, double durationMs)
    {
        var sb = new StringBuilder();
        var position = 0;
        while (true)
        {
            var index = s.IndexOf("\\t(", position, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            var depth = 1;
            var end = index + 3;
            while (end < s.Length && depth > 0)
            {
                if (s[end] == '(')
                {
                    depth++;
                }
                else if (s[end] == ')')
                {
                    depth--;
                }

                end++;
            }

            if (depth != 0)
            {
                break;
            }

            var args = SplitTopLevel(s.Substring(index + 3, end - index - 4));
            var numbers = args.TakeWhile(a => !a.TrimStart().StartsWith('\\')).ToList();
            var tags = string.Join(",", args.Skip(numbers.Count));
            double t1 = 0, t2 = durationMs;
            string? accel = null;
            switch (numbers.Count)
            {
                case 1:
                    accel = numbers[0].Trim();
                    break;
                case 2:
                case 3:
                    t1 = ParseNumber(numbers[0]);
                    t2 = ParseNumber(numbers[1]);
                    accel = numbers.Count == 3 ? numbers[2].Trim() : null;
                    break;
            }

            t1 -= offsetMs;
            t2 -= offsetMs;
            if (Math.Round(t2) == 0)
            {
                // libass treats t2 = 0 as "until the end of the event"
                t2 = -1;
                t1 = Math.Min(t1, -2);
            }

            sb.Append(s, position, index - position);
            sb.Append($"\\t({I(t1)},{I(t2)},{(accel != null ? accel + "," : string.Empty)}{tags})");
            position = end;
        }

        sb.Append(s, position, s.Length - position);
        return sb.ToString();
    }

    /// <summary>The override block the line starts with, without its \t(...) animations (they don't set the start value).</summary>
    private static string LeadingTags(string s)
    {
        var end = s.StartsWith('{') ? s.IndexOf('}') : -1;
        return end < 0 ? string.Empty : TransformRegex.Replace(s[..end], string.Empty);
    }

    private static List<string> SplitTopLevel(string s)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '(')
            {
                depth++;
            }
            else if (s[i] == ')')
            {
                depth--;
            }
            else if (s[i] == ',' && depth == 0)
            {
                result.Add(s.Substring(start, i - start));
                start = i + 1;
            }
        }

        result.Add(s.Substring(start));
        return result;
    }

    private static string TranslateDrawing(string drawing, double dx, double dy)
    {
        var sb = new StringBuilder();
        var isX = true;
        foreach (var token in drawing.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                sb.Append(F(v + (isX ? dx : dy)));
                isX = !isX;
            }
            else
            {
                sb.Append(token);
            }

            sb.Append(' ');
        }

        return sb.ToString().TrimEnd();
    }

    private static double ParseNumber(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static string F(double v) => (Math.Round(v, 2) + 0.0).ToString("0.##", CultureInfo.InvariantCulture); // + 0.0 turns -0 into 0

    private static string I(double v) => ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
}
