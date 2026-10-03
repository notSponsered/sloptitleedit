using Nikse.SubtitleEdit.UiLogic.Assa;
using SkiaSharp;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>
/// The colour under a shape, per frame: median of the pixels well inside it (edges and small tracking errors are skipped).
/// Frames are ffmpeg JPEGs, which keep the video's YCbCr matrix and are decoded as BT.601 - exactly what libass/mpv show for
/// a script without "YCbCr Matrix" (or TV.601); for a 709 script the colour gets one matrix correction.
/// ponytail: very saturated colours of BT.709 video clip a little in the JPEG; an rgb24 extraction fixes that if it matters.
/// </summary>
public static class ShapeColor
{
    /// <param name="toVideoX">Video px per frame px (horizontal).</param>
    public static SKColor? Sample(SKBitmap frame, PenPath shape, double toVideoX, double toVideoY)
    {
        var polygon = ShapeMorph.Flatten(shape).Select(p => (X: p.X / toVideoX, Y: p.Y / toVideoY)).ToList();
        if (polygon.Count < 3)
        {
            return null;
        }

        var left = Math.Max(0, polygon.Min(p => p.X));
        var top = Math.Max(0, polygon.Min(p => p.Y));
        var right = Math.Min(frame.Width - 1, polygon.Max(p => p.X));
        var bottom = Math.Min(frame.Height - 1, polygon.Max(p => p.Y));
        if (right <= left || bottom <= top)
        {
            return null;
        }

        var erosion = 0.1 * Math.Sqrt(Math.Abs(ShapeMorph.SignedArea(polygon)) / Math.PI);
        var step = Math.Max(1, Math.Sqrt((right - left) * (bottom - top) / 2000));
        var inner = new List<SKColor>();
        var all = new List<SKColor>();
        for (var y = top + step / 2; y <= bottom; y += step)
        {
            for (var x = left + step / 2; x <= right; x += step)
            {
                if (!Inside(polygon, x, y))
                {
                    continue;
                }

                var color = frame.GetPixel((int)x, (int)y);
                all.Add(color);
                if (DistanceToOutline(polygon, x, y) >= erosion)
                {
                    inner.Add(color);
                }
            }
        }

        var samples = inner.Count > 0 ? inner : all;
        return samples.Count == 0 ? null : Median(samples);
    }

    private static SKColor Median(List<SKColor> colors)
    {
        static byte M(IEnumerable<byte> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        return new SKColor(M(colors.Select(c => c.Red)), M(colors.Select(c => c.Green)), M(colors.Select(c => c.Blue)));
    }

    /// <summary>Even-odd rule.</summary>
    private static bool Inside(List<(double X, double Y)> polygon, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double DistanceToOutline(List<(double X, double Y)> polygon, double x, double y)
    {
        var best = double.MaxValue;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (ax, ay) = polygon[j];
            var (bx, by) = polygon[i];
            var dx = bx - ax;
            var dy = by - ay;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared > 0 ? Math.Clamp(((x - ax) * dx + (y - ay) * dy) / lengthSquared, 0, 1) : 0;
            var ex = ax + t * dx - x;
            var ey = ay + t * dy - y;
            best = Math.Min(best, ex * ex + ey * ey);
        }

        return Math.Sqrt(best);
    }

    /// <summary>Gaps take the nearest sampled colour, then a 5-frame median removes flicker and one-frame spikes.</summary>
    public static List<SKColor> Smooth(IReadOnlyList<SKColor?> colors)
    {
        var filled = new SKColor[colors.Count];
        for (var i = 0; i < colors.Count; i++)
        {
            var nearest = Enumerable.Range(0, colors.Count).Where(k => colors[k] != null).DefaultIfEmpty(-1).MinBy(k => Math.Abs(k - i));
            filled[i] = nearest >= 0 ? colors[nearest]!.Value : SKColors.White;
        }

        return Enumerable.Range(0, filled.Length).Select(i =>
        {
            var window = filled.Skip(Math.Max(0, i - 2)).Take(Math.Min(filled.Length, i + 3) - Math.Max(0, i - 2)).ToList();
            return Median(window);
        }).ToList();
    }

    /// <summary>
    /// True when libass/mpv show the script's colours unconverted on BT.709 video, i.e. the colour needs <see cref="To709"/>:
    /// TV/PC.709, or None on video players decode as 709 (mpv/libplacebo guess: width ≥ 1280 or height > 576). Missing (libass default) and 601 values mean VSFilter-compatible conversion - no correction.
    /// </summary>
    public static bool NeedsBt709Correction(string header, int videoWidth, int videoHeight)
    {
        var matrix = AssaScriptInfo.Get(header, "YCbCr Matrix")?.Trim() ?? string.Empty;
        if (matrix.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return videoWidth >= 1280 || videoHeight > 576;
        }

        return matrix.Contains("709", StringComparison.Ordinal);
    }

    /// <summary>A colour decoded with BT.601 re-decoded with BT.709 (full range).</summary>
    public static SKColor To709(SKColor c)
    {
        var y = 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
        var cb = (c.Blue - y) / 1.772;
        var cr = (c.Red - y) / 1.402;
        static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        return new SKColor(B(y + 1.5748 * cr), B(y - 0.1873 * cb - 0.4681 * cr), B(y + 1.8556 * cb));
    }
}
