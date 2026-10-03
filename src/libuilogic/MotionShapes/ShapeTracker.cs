using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using SkiaSharp;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>A keyframe shape (closed, video px) drawn on extracted frame <see cref="Frame"/>.</summary>
public sealed record ShapeKey(int Frame, PenPath Path);

/// <summary>The computed shape for one extracted frame (video px).</summary>
public sealed class ShapeFrame
{
    public required int Index { get; init; }
    public required PenPath Path { get; init; }

    /// <summary>The edges didn't match well here - the shape is guessed, a key near this frame fixes it.</summary>
    public bool Interpolated { get; init; }

    /// <summary>Only when colour tracking is on.</summary>
    public SKColor? Color { get; set; }
}

/// <summary>
/// The shape on every frame. With video: each key is followed through the frames by its outline (<see cref="ShapeFitter"/>),
/// forward from one key and backward from the next, blended by time and by how well each matched, so keys are exact and
/// the shape in between comes from the picture. Frames identical to the previous one keep its shape (held drawings).
/// Without video: plain interpolation between the keys.
/// </summary>
public static class ShapeTracker
{
    /// <summary>Below this edge-match confidence a frame is marked as guessed.</summary>
    public const double MinConfidence = 0.2;

    /// <summary>One shape per entry of <paramref name="timesMs"/> (extracted frame times).</summary>
    public static List<ShapeFrame> Compute(IReadOnlyList<ShapeKey> keys, IReadOnlyList<double> timesMs, ShapeVideo? video, bool scaleAndRotation,
        CancellationToken cancellationToken)
    {
        var n = timesMs.Count;
        var result = new ShapeFrame?[n];
        var sorted = keys.Where(k => k.Frame >= 0 && k.Frame < n).GroupBy(k => k.Frame).Select(g => g.Last()).OrderBy(k => k.Frame).ToList();
        if (sorted.Count == 0)
        {
            return [];
        }

        // before the first and after the last key: that key, followed (or held)
        Fill(result, sorted[0], 0, video, scaleAndRotation, cancellationToken);
        Fill(result, sorted[^1], n - 1, video, scaleAndRotation, cancellationToken);
        for (var i = 0; i + 1 < sorted.Count; i++)
        {
            Between(result, sorted[i], sorted[i + 1], timesMs, video, scaleAndRotation, cancellationToken);
        }

        foreach (var key in sorted)
        {
            result[key.Frame] = new ShapeFrame { Index = key.Frame, Path = key.Path.Clone() };
        }

        if (video != null)
        {
            // held drawings (animation on 2s/3s) get exactly one shape, so they become one line.
            // A key inside a held drawing gives the whole drawing its shape, also the frames before it.
            var fixedShape = sorted.Select(k => k.Frame).ToHashSet();
            foreach (var key in sorted)
            {
                var bounds = FrameBounds(key.Path, video);
                for (var f = key.Frame - 1; f >= 0 && !fixedShape.Contains(f) && ShapeFitter.IsHeld(video.Gray(key.Frame), video.Gray(f), bounds); f--)
                {
                    result[f] = new ShapeFrame { Index = f, Path = key.Path.Clone() };
                    fixedShape.Add(f);
                }
            }

            var holdStart = 0; // compared with the first frame of the drawing, so a slow pan adds up and isn't frozen
            for (var f = 1; f < n; f++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fixedShape.Contains(f))
                {
                    holdStart = f;
                    continue;
                }

                var start = result[holdStart]!;
                var previous = result[f - 1]!;
                if (ShapeFitter.IsHeld(video.Gray(holdStart), video.Gray(f), FrameBounds(start.Path, video)))
                {
                    result[f] = new ShapeFrame { Index = f, Path = start.Path.Clone(), Interpolated = start.Interpolated };
                    continue;
                }

                holdStart = f;
                if (Moved(previous.Path, result[f]!.Path, RigidTolerance * Math.Max(video.ToVideoX, video.ToVideoY)) is { } moved)
                {
                    // only moved: the exact previous shape, so edge noise doesn't split a slide into many lines
                    result[f] = new ShapeFrame { Index = f, Path = moved, Interpolated = result[f]!.Interpolated };
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result.Select(f => f!).ToList();
    }

    private static void Fill(ShapeFrame?[] result, ShapeKey key, int to, ShapeVideo? video, bool scaleAndRotation, CancellationToken cancellationToken)
    {
        if (key.Frame == to)
        {
            return;
        }

        var step = to > key.Frame ? 1 : -1;
        var fitted = video == null ? null : ShapeFitter.Follow(video, key.Path, key.Frame, to, scaleAndRotation, cancellationToken);
        for (var f = key.Frame + step; f != to + step; f += step)
        {
            var k = (f - key.Frame) * step;
            result[f] = fitted == null
                ? new ShapeFrame { Index = f, Path = key.Path.Clone() }
                : new ShapeFrame { Index = f, Path = fitted[k].Path, Interpolated = fitted[k].Confidence < MinConfidence };
        }
    }

    private static void Between(ShapeFrame?[] result, ShapeKey a, ShapeKey b, IReadOnlyList<double> timesMs, ShapeVideo? video, bool scaleAndRotation,
        CancellationToken cancellationToken)
    {
        var t0 = timesMs[a.Frame];
        var duration = timesMs[b.Frame] - t0;
        if (video == null)
        {
            var morph = ShapeMorph.Morpher(a.Path, b.Path);
            for (var f = a.Frame + 1; f < b.Frame; f++)
            {
                result[f] = new ShapeFrame { Index = f, Path = morph(duration > 0 ? (timesMs[f] - t0) / duration : 1) };
            }

            return;
        }

        var forward = ShapeFitter.Follow(video, a.Path, a.Frame, b.Frame, scaleAndRotation, cancellationToken);
        var backward = ShapeFitter.Follow(video, b.Path, b.Frame, a.Frame, scaleAndRotation, cancellationToken);

        // the passes keep their key's anchors; match them up once (per frame it could rotate on a symmetric shape)
        (bool Reversed, int Shift)? alignment = a.Path.Anchors.Count == b.Path.Anchors.Count ? ShapeMorph.Alignment(a.Path, b.Path) : null;
        for (var f = a.Frame + 1; f < b.Frame; f++)
        {
            var u = duration > 0 ? (timesMs[f] - t0) / duration : 1;
            var fromA = forward[f - a.Frame];
            var fromB = backward[b.Frame - f];

            // the nearer key counts more, and so does the pass that matched the edges better
            var wa = (1 - u) * fromA.Confidence;
            var wb = u * fromB.Confidence;
            var w = wa + wb > 1e-9 ? wb / (wa + wb) : u;
            var confidence = (1 - w) * fromA.Confidence + w * fromB.Confidence;
            var path = alignment is { } match
                ? ShapeMorph.Lerp(fromA.Path, ShapeMorph.Reorder(fromB.Path, match), w)
                : ShapeMorph.Morpher(fromA.Path, fromB.Path)(w);
            result[f] = new ShapeFrame { Index = f, Path = path, Interpolated = confidence < MinConfidence };
        }
    }

    /// <summary>Frame px a fitted shape may differ from a moved copy of the previous one and still count as "only moved".</summary>
    private const double RigidTolerance = 1.0;

    /// <summary><paramref name="previous"/> moved onto <paramref name="current"/>, when every point is within <paramref name="tolerance"/>; else null.</summary>
    private static PenPath? Moved(PenPath previous, PenPath current, double tolerance)
    {
        var a = previous.Anchors;
        var b = current.Anchors;
        if (a.Count != b.Count || a.Count == 0)
        {
            return null;
        }

        var dx = Enumerable.Range(0, a.Count).Average(i => b[i].X - a[i].X);
        var dy = Enumerable.Range(0, a.Count).Average(i => b[i].Y - a[i].Y);
        for (var i = 0; i < a.Count; i++)
        {
            if (Off(a[i].X, a[i].Y, b[i].X, b[i].Y) || Off(a[i].InX, a[i].InY, b[i].InX, b[i].InY) || Off(a[i].OutX, a[i].OutY, b[i].OutX, b[i].OutY))
            {
                return null;
            }
        }

        return new Similarity(0, 0, dx, dy, 1, 0).Apply(previous);

        bool Off(double ax, double ay, double bx, double by) => Math.Abs(ax + dx - bx) > tolerance || Math.Abs(ay + dy - by) > tolerance;
    }

    private static TrackBox FrameBounds(PenPath path, ShapeVideo video)
    {
        var box = Bounds(path);
        return new TrackBox(box.Left / video.ToVideoX, box.Top / video.ToVideoY, box.Width / video.ToVideoX, box.Height / video.ToVideoY);
    }

    public static TrackBox Bounds(PenPath path)
    {
        var points = ShapeMorph.Flatten(path);
        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        return new TrackBox(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
    }
}
