using SkiaSharp;

namespace Nikse.SubtitleEdit.UiLogic.MotionTracking;

/// <summary>
/// 8-bit grayscale image.
/// </summary>
public sealed class GrayFrame
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public GrayFrame(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static GrayFrame FromFile(string fileName)
    {
        using var source = SKBitmap.Decode(fileName) ?? throw new InvalidOperationException("Unable to decode " + fileName);
        using var bitmap = source.ColorType == SKColorType.Bgra8888 ? null : source.Copy(SKColorType.Bgra8888);
        var bgra = (bitmap ?? source).GetPixelSpan();
        var rowBytes = (bitmap ?? source).RowBytes;
        var width = source.Width; // SKBitmap.Width/Height are native calls - keep them out of the loop
        var height = source.Height;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * rowBytes, width * 4);
            var offset = y * width;
            for (var x = 0; x < width; x++)
            {
                // Rec. 601 luma
                pixels[offset + x] = (byte)((row[x * 4] * 29 + row[x * 4 + 1] * 150 + row[x * 4 + 2] * 77) >> 8);
            }
        }

        return new GrayFrame(width, height, pixels);
    }

    public GrayFrame Downsample(int factor)
    {
        if (factor <= 1)
        {
            return this;
        }

        var w = Width / factor;
        var h = Height / factor;
        var result = new byte[w * h];
        var area = factor * factor;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var sum = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    var row = (y * factor + dy) * Width + x * factor;
                    for (var dx = 0; dx < factor; dx++)
                    {
                        sum += Pixels[row + dx];
                    }
                }

                result[y * w + x] = (byte)(sum / area);
            }
        }

        return new GrayFrame(w, h, result);
    }

    /// <summary>
    /// Bilinear sample at pixel-index coordinates (clamped to the image).
    /// </summary>
    public double Sample(double x, double y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var row0 = y0 * Width;
        var row1 = y1 * Width;
        var top = Pixels[row0 + x0] + (Pixels[row0 + x1] - Pixels[row0 + x0]) * fx;
        var bottom = Pixels[row1 + x0] + (Pixels[row1 + x1] - Pixels[row1 + x0]) * fx;
        return top + (bottom - top) * fy;
    }
}

public readonly record struct TrackBox(double Left, double Top, double Width, double Height)
{
    public double CenterX => Left + Width / 2.0;
    public double CenterY => Top + Height / 2.0;
}


/// <summary>
/// Tracked state for one frame, relative to the frame tracking started from:
/// anchor point (center of all boxes, frame pixels), scale factor, rotation in degrees (clockwise on screen),
/// match score (0-1) and every box's center (for display).
/// </summary>
public readonly record struct TrackPoint(int Index, double X, double Y, double Scale, double Rotation, double Score)
{
    public (double X, double Y)[]? Centers { get; init; }
}

/// <summary>
/// Simple template tracker based on zero-mean normalized cross-correlation (NCC). Each box ("point") is tracked on its own;
/// several boxes are combined robustly (outliers are snapped back), which keeps tracking when one point is covered.
/// Position only: coarse-to-fine search + sub-pixel peak, with a "previous frame" template for gradual appearance change.
/// Scale + rotation: one box is matched against the frame warped by position/scale/rotation; with 2+ boxes
/// scale/rotation come from how the boxes move relative to each other.
/// Big jumps (shakes, animation on 2s/3s, dropped frames): the "held still" position is tried too, then a wide coarse search.
/// </summary>
public static class MotionTracker
{
    /// <summary>Below this score a point is considered lost (tunable).</summary>
    public const double MinScore = 0.6;

    /// <summary>The original template is used when it matches at least this well (no drift).</summary>
    public const double OriginalTemplateScore = 0.85;

    /// <summary>Boxes flatter than this (gray-level standard deviation) can't be tracked.</summary>
    public const double MinStdDev = 4;

    private const int CoarseTemplateSize = 48;

    public static bool HasEnoughDetail(GrayFrame frame, TrackBox box)
    {
        var t = Template.From(frame, (int)Math.Round(box.Left), (int)Math.Round(box.Top), (int)Math.Round(box.Width), (int)Math.Round(box.Height));
        return t != null && t.StdDev >= MinStdDev;
    }

    public static List<TrackPoint> Track(Func<int, GrayFrame> loadFrame, int fromIndex, int toIndex, TrackBox box,
        bool scaleAndRotation, CancellationToken cancellationToken, IProgress<int>? progress = null) =>
        Track(loadFrame, fromIndex, toIndex, [box], scaleAndRotation, cancellationToken, progress);

    /// <summary>
    /// Tracks the boxes from frame <paramref name="fromIndex"/> to <paramref name="toIndex"/> (either direction).
    /// The first point is the start state. Stops early when every box is lost, or on cancel.
    /// </summary>
    public static List<TrackPoint> Track(Func<int, GrayFrame> loadFrame, int fromIndex, int toIndex, IReadOnlyList<TrackBox> boxes,
        bool scaleAndRotation, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        var first = new FrameLevels(loadFrame(fromIndex));
        var trackers = boxes
            .Select(b => scaleAndRotation ? (BoxTracker?)SimilarityTracker.Create(first, b) : TranslationTracker.Create(first, b))
            .Where(t => t != null)
            .Select(t => t!)
            .ToList();
        var anchorX = boxes.Count > 0 ? boxes.Average(b => b.CenterX) : 0;
        var anchorY = boxes.Count > 0 ? boxes.Average(b => b.CenterY) : 0;
        var result = new List<TrackPoint>
        {
            new(fromIndex, anchorX, anchorY, 1, 0, 1) { Centers = trackers.Select(t => (t.X, t.Y)).ToArray() },
        };

        if (trackers.Count == 0)
        {
            return result;
        }

        foreach (var (index, frame) in Frames(loadFrame, fromIndex, toIndex, cancellationToken))
        {
            var levels = new FrameLevels(frame);
            foreach (var tracker in trackers.Where(t => !t.Lost))
            {
                tracker.Update(levels);
            }

            var alive = trackers.Where(t => !t.Lost).ToList();
            if (alive.Count == 0)
            {
                break;
            }

            var fit = Combine(alive, scaleAndRotation);
            var (x, y) = Transform(fit, anchorX, anchorY);
            result.Add(new TrackPoint(index, x, y, fit.Scale, fit.Theta * 180.0 / Math.PI, alive.Average(t => t.Score))
            {
                Centers = trackers.Select(t => t.Lost ? Transform(fit, t.StartX, t.StartY) : (t.X, t.Y)).ToArray(),
            });
            progress?.Report(result.Count);
        }

        return result;
    }

    /// <summary>
    /// Similarity transform (start frame -> current frame) from the boxes still tracked. Points that disagree with the others
    /// (e.g. jumped to a look-alike) are snapped back to where the rest says they are.
    /// </summary>
    private static (double Scale, double Theta, double Tx, double Ty) Combine(List<BoxTracker> alive, bool scaleAndRotation)
    {
        if (alive.Count == 1)
        {
            // x -> c + s * R * (x - c0)
            var t = alive[0];
            var (rx, ry) = Transform((t.Scale, t.Theta, 0, 0), t.StartX, t.StartY);
            return (t.Scale, t.Theta, t.X - rx, t.Y - ry);
        }

        (double Scale, double Theta, double Tx, double Ty) fit;
        List<BoxTracker> inliers;
        if (!scaleAndRotation)
        {
            var medianX = Median(alive.Select(t => t.X - t.StartX));
            var medianY = Median(alive.Select(t => t.Y - t.StartY));
            inliers = alive.Where(t => Math.Abs(t.X - t.StartX - medianX) <= 3 && Math.Abs(t.Y - t.StartY - medianY) <= 3).ToList();
            if (inliers.Count == 0)
            {
                inliers = alive;
            }

            fit = (1, 0, inliers.Average(t => t.X - t.StartX), inliers.Average(t => t.Y - t.StartY));
        }
        else
        {
            fit = FitSimilarity(alive);
            inliers = alive;
            if (alive.Count >= 3)
            {
                var residuals = alive.Select(t => Residual(fit, t)).ToList();
                var limit = Math.Max(3, Median(residuals) * 2.5);
                inliers = alive.Where((_, i) => residuals[i] <= limit).ToList();
                if (inliers.Count >= 2)
                {
                    fit = FitSimilarity(inliers);
                }
                else
                {
                    inliers = alive;
                }
            }
        }

        foreach (var outlier in alive.Except(inliers))
        {
            var (x, y) = Transform(fit, outlier.StartX, outlier.StartY);
            outlier.SnapTo(x, y);
        }

        return fit;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static (double X, double Y) Transform((double Scale, double Theta, double Tx, double Ty) fit, double x, double y) =>
        (fit.Scale * (Math.Cos(fit.Theta) * x - Math.Sin(fit.Theta) * y) + fit.Tx,
         fit.Scale * (Math.Sin(fit.Theta) * x + Math.Cos(fit.Theta) * y) + fit.Ty);

    private static double Residual((double Scale, double Theta, double Tx, double Ty) fit, BoxTracker t)
    {
        var (x, y) = Transform(fit, t.StartX, t.StartY);
        return Math.Sqrt((x - t.X) * (x - t.X) + (y - t.Y) * (y - t.Y));
    }

    /// <summary>Closed-form least-squares similarity from the start centers to the current centers.</summary>
    private static (double Scale, double Theta, double Tx, double Ty) FitSimilarity(List<BoxTracker> points)
    {
        var mpx = points.Average(p => p.StartX);
        var mpy = points.Average(p => p.StartY);
        var mqx = points.Average(p => p.X);
        var mqy = points.Average(p => p.Y);
        double a = 0, b = 0, norm = 0;
        foreach (var p in points)
        {
            var px = p.StartX - mpx;
            var py = p.StartY - mpy;
            var qx = p.X - mqx;
            var qy = p.Y - mqy;
            a += px * qx + py * qy;
            b += px * qy - py * qx;
            norm += px * px + py * py;
        }

        var theta = Math.Atan2(b, a);
        var scale = norm > 0 ? Math.Sqrt(a * a + b * b) / norm : 1;
        var (rx, ry) = Transform((scale, theta, 0, 0), mpx, mpy);
        return (scale, theta, mqx - rx, mqy - ry);
    }

    /// <summary>
    /// Frames after <paramref name="fromIndex"/> up to and including <paramref name="toIndex"/>; the next frame is decoded
    /// in the background while the current one is matched.
    /// </summary>
    private static IEnumerable<(int Index, GrayFrame Frame)> Frames(Func<int, GrayFrame> loadFrame, int fromIndex, int toIndex, CancellationToken cancellationToken)
    {
        if (fromIndex == toIndex)
        {
            yield break;
        }

        var step = toIndex > fromIndex ? 1 : -1;
        var next = Task.Run(() => loadFrame(fromIndex + step));
        for (var index = fromIndex + step; ; index += step)
        {
            var frame = next.Result;
            if (index != toIndex)
            {
                var nextIndex = index + step;
                next = Task.Run(() => loadFrame(nextIndex));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return (index, frame);
            if (index == toIndex)
            {
                yield break;
            }
        }
    }

    /// <summary>A frame plus its downsampled versions (made on demand, shared by all boxes).</summary>
    private sealed class FrameLevels(GrayFrame frame)
    {
        private readonly Dictionary<int, GrayFrame> _levels = new() { [1] = frame };

        public GrayFrame Frame => frame;

        public GrayFrame Get(int factor)
        {
            if (!_levels.TryGetValue(factor, out var level))
            {
                level = frame.Downsample(factor);
                _levels[factor] = level;
            }

            return level;
        }
    }

    /// <summary>A template cut from a frame, with coarse versions made on demand.</summary>
    private sealed class Patch
    {
        private readonly FrameLevels _source;
        private readonly int _left;
        private readonly int _top;
        private readonly Dictionary<int, Template?> _coarse = [];

        public Template Full { get; }

        private Patch(FrameLevels source, int left, int top, Template full)
        {
            _source = source;
            _left = left;
            _top = top;
            Full = full;
        }

        public static Patch? From(FrameLevels source, int left, int top, int width, int height)
        {
            var full = Template.From(source.Frame, left, top, width, height);
            return full == null ? null : new Patch(source, left, top, full);
        }

        public Template? Coarse(int factor)
        {
            if (factor <= 1)
            {
                return Full;
            }

            if (!_coarse.TryGetValue(factor, out var template))
            {
                template = Full.Width / factor < 4 || Full.Height / factor < 4
                    ? null
                    : Template.From(_source.Get(factor), (int)Math.Round((double)_left / factor), (int)Math.Round((double)_top / factor), Full.Width / factor, Full.Height / factor);
                _coarse[factor] = template;
            }

            return template;
        }
    }

    /// <summary>
    /// Finds the patch near the predicted position. Also tries "didn't move" (animation on 2s/3s) and, when both fail,
    /// a wide search on a coarser level (camera shake, dropped frames).
    /// Offsets give the tracked point relative to the patch's top-left.
    /// </summary>
    private static (double X, double Y, double Score) Search(FrameLevels frame, Patch patch, double offsetX, double offsetY,
        double x, double y, double velocityX, double velocityY, int factor, int radius)
    {
        var best = SearchAt(frame, patch, offsetX, offsetY, x + velocityX, y + velocityY, factor, radius);
        if (best.Score < OriginalTemplateScore && (velocityX != 0 || velocityY != 0))
        {
            var held = SearchAt(frame, patch, offsetX, offsetY, x, y, factor, radius);
            if (held.Score > best.Score)
            {
                best = held;
            }
        }

        if (best.Score < MinScore)
        {
            var wideFactor = factor * 2;
            var wideRadius = Math.Min(Math.Max(radius * 4, 320), Math.Max(frame.Frame.Width, frame.Frame.Height) / 2);
            if (patch.Coarse(wideFactor) != null)
            {
                var wide = SearchAt(frame, patch, offsetX, offsetY, x, y, wideFactor, wideRadius);
                if (wide.Score > best.Score)
                {
                    best = wide;
                }
            }
        }

        return best;
    }

    private static (double X, double Y, double Score) SearchAt(FrameLevels frame, Patch patch, double offsetX, double offsetY,
        double predictedX, double predictedY, int factor, int radius)
    {
        var coarseTemplate = patch.Coarse(factor);
        if (coarseTemplate == null)
        {
            factor = 1;
            coarseTemplate = patch.Full;
        }

        var predictedLeft = predictedX - offsetX;
        var predictedTop = predictedY - offsetY;
        Match fine;
        if (factor > 1)
        {
            var c = MatchAt(frame.Get(factor), coarseTemplate, (int)Math.Round(predictedLeft / factor), (int)Math.Round(predictedTop / factor), (int)Math.Ceiling((double)radius / factor));
            if (c.Score <= -1)
            {
                return (0, 0, -1);
            }

            fine = MatchAt(frame.Frame, patch.Full, c.Left * factor, c.Top * factor, (int)Math.Ceiling(factor / 2.0) + 1);
        }
        else
        {
            fine = MatchAt(frame.Frame, patch.Full, (int)Math.Round(predictedLeft), (int)Math.Round(predictedTop), radius);
        }

        return (fine.Left + fine.SubX + offsetX, fine.Top + fine.SubY + offsetY, fine.Score);
    }

    /// <summary>One tracked box. X/Y/Start are the box center in frame pixels (same convention as TrackBox.CenterX).</summary>
    private abstract class BoxTracker
    {
        public double StartX { get; protected init; }
        public double StartY { get; protected init; }
        public double X { get; protected set; }
        public double Y { get; protected set; }
        public double VelocityX { get; protected set; }
        public double VelocityY { get; protected set; }
        public double Scale { get; protected set; } = 1;
        public double Theta { get; protected set; }
        public double Score { get; protected set; } = 1;
        public bool Lost { get; protected set; }
        protected int Factor { get; init; }
        protected int Radius { get; init; }

        public abstract void Update(FrameLevels frame);

        public virtual void SnapTo(double x, double y)
        {
            X = x;
            Y = y;
            VelocityX = 0;
            VelocityY = 0;
        }

        protected static (int Factor, int Radius) SearchSize(double width, double height) =>
            (Math.Max(1, (int)Math.Ceiling(Math.Max(width, height) / CoarseTemplateSize)), (int)Math.Clamp(Math.Max(width, height) / 2.0, 24, 160));
    }

    private sealed class TranslationTracker : BoxTracker
    {
        private Patch _original = null!;
        private double _originalOffsetX;
        private double _originalOffsetY;
        private Patch? _previous;
        private double _previousOffsetX;
        private double _previousOffsetY;

        public static TranslationTracker? Create(FrameLevels frame, TrackBox box)
        {
            var left = (int)Math.Round(box.Left);
            var top = (int)Math.Round(box.Top);
            var original = Patch.From(frame, left, top, (int)Math.Round(box.Width), (int)Math.Round(box.Height));
            if (original == null || original.Full.StdDev < MinStdDev)
            {
                return null;
            }

            var (factor, radius) = SearchSize(box.Width, box.Height);
            return new TranslationTracker
            {
                _original = original,
                _originalOffsetX = box.CenterX - left,
                _originalOffsetY = box.CenterY - top,
                StartX = box.CenterX,
                StartY = box.CenterY,
                X = box.CenterX,
                Y = box.CenterY,
                Factor = factor,
                Radius = radius,
            };
        }

        public override void Update(FrameLevels frame)
        {
            var best = Search(frame, _original, _originalOffsetX, _originalOffsetY, X, Y, VelocityX, VelocityY, Factor, Radius);
            if (best.Score < OriginalTemplateScore && _previous != null)
            {
                var fromPrevious = Search(frame, _previous, _previousOffsetX, _previousOffsetY, X, Y, VelocityX, VelocityY, Factor, Radius);
                if (fromPrevious.Score > best.Score)
                {
                    best = fromPrevious;
                }
            }

            if (best.Score < MinScore)
            {
                Lost = true;
                return;
            }

            VelocityX = best.X - X;
            VelocityY = best.Y - Y;
            X = best.X;
            Y = best.Y;
            Score = best.Score;

            // refresh the "previous frame" template at the new position (handles gradual appearance change)
            var left = (int)Math.Round(X - _originalOffsetX);
            var top = (int)Math.Round(Y - _originalOffsetY);
            _previous = Patch.From(frame, left, top, _original.Full.Width, _original.Full.Height);
            _previousOffsetX = X - left;
            _previousOffsetY = Y - top;
        }

        public override void SnapTo(double x, double y)
        {
            base.SnapTo(x, y);
            _previous = null; // must match its original template again
        }
    }

    /// <summary>
    /// The original box pixels, scored against the current frame sampled at center + scale * R(theta) * offset.
    /// </summary>
    private sealed class SimilarityTracker : BoxTracker
    {
        private const int MaxSamples = 6000;

        private GrayFrame _reference = null!;
        private int _width;
        private int _height;
        private double _referenceX; // pixel-index coordinates of the box center in the start frame
        private double _referenceY;
        private double[] _dx = [];
        private double[] _dy = [];
        private double[] _values = []; // zero-mean
        private double _norm;
        private double _scaleVelocity;
        private double _thetaVelocity;

        public static SimilarityTracker? Create(FrameLevels frame, TrackBox box)
        {
            var left = (int)Math.Round(box.Left);
            var top = (int)Math.Round(box.Top);
            var width = (int)Math.Round(box.Width);
            var height = (int)Math.Round(box.Height);
            var reference = frame.Frame;
            if (width < 4 || height < 4 || left < 0 || top < 0 || left + width > reference.Width || top + height > reference.Height)
            {
                return null;
            }

            var stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)width * height / MaxSamples)));
            var dx = new List<double>();
            var dy = new List<double>();
            var values = new List<double>();
            for (var v = 0; v < height; v += stride)
            {
                for (var u = 0; u < width; u += stride)
                {
                    dx.Add(u - (width - 1) / 2.0);
                    dy.Add(v - (height - 1) / 2.0);
                    values.Add(reference.Pixels[(top + v) * reference.Width + left + u]);
                }
            }

            var mean = values.Average();
            var zeroMean = values.Select(value => value - mean).ToArray();
            var norm = Math.Sqrt(zeroMean.Sum(value => value * value));
            if (norm / Math.Sqrt(zeroMean.Length) < MinStdDev)
            {
                return null;
            }

            var (factor, radius) = SearchSize(width, height);
            var referenceX = left + (width - 1) / 2.0;
            var referenceY = top + (height - 1) / 2.0;
            return new SimilarityTracker
            {
                _reference = reference,
                _width = width,
                _height = height,
                _referenceX = referenceX,
                _referenceY = referenceY,
                _dx = dx.ToArray(),
                _dy = dy.ToArray(),
                _values = zeroMean,
                _norm = norm,
                StartX = referenceX + 0.5,
                StartY = referenceY + 0.5,
                X = referenceX + 0.5,
                Y = referenceY + 0.5,
                Factor = factor,
                Radius = radius,
            };
        }

        /// <summary>
        /// Translation search with the box warped to the predicted scale/rotation, then local refinement of all four parameters.
        /// </summary>
        public override void Update(FrameLevels frame)
        {
            var scale = Scale + _scaleVelocity;
            var theta = Theta + _thetaVelocity;
            var warped = new FrameLevels(Warp(scale, theta));
            var patch = Patch.From(warped, 0, 0, _width, _height);
            double x = X - 0.5, y = Y - 0.5; // pixel-index coordinates
            if (patch != null)
            {
                var m = Search(frame, patch, (_width - 1) / 2.0, (_height - 1) / 2.0, x, y, VelocityX, VelocityY, Factor, Radius);
                if (m.Score > -1)
                {
                    (x, y) = (m.X, m.Y);
                }
            }

            var refined = Refine(frame.Frame, x, y, scale, theta);
            if (refined.Score < MinScore)
            {
                Lost = true;
                return;
            }

            VelocityX = refined.X + 0.5 - X;
            VelocityY = refined.Y + 0.5 - Y;
            _scaleVelocity = refined.Scale - Scale;
            _thetaVelocity = refined.Theta - Theta;
            X = refined.X + 0.5;
            Y = refined.Y + 0.5;
            Scale = refined.Scale;
            Theta = refined.Theta;
            Score = refined.Score;
        }

        public override void SnapTo(double x, double y)
        {
            base.SnapTo(x, y);
            _scaleVelocity = 0;
            _thetaVelocity = 0;
        }

        private (double X, double Y, double Scale, double Theta, double Score) Refine(GrayFrame frame, double x, double y, double scale, double theta)
        {
            var p = new[] { x, y, scale, theta };
            var steps = new[] { 1.0, 1.0, 0.01, 0.5 * Math.PI / 180.0 };
            var best = ScoreAt(frame, p);
            for (var iteration = 0; iteration < 4; iteration++)
            {
                for (var k = 0; k < p.Length; k++)
                {
                    var original = p[k];
                    p[k] = original - steps[k];
                    var minus = ScoreAt(frame, p);
                    p[k] = original + steps[k];
                    var plus = ScoreAt(frame, p);

                    var denominator = minus - 2 * best + plus;
                    var offset = denominator < 0 ? Math.Clamp((minus - plus) / (2 * denominator), -1, 1) : plus > minus ? 1 : -1;
                    p[k] = original + offset * steps[k];
                    var parabola = ScoreAt(frame, p);

                    var (value, score) = (original, best);
                    if (parabola > score) (value, score) = (p[k], parabola);
                    if (minus > score) (value, score) = (original - steps[k], minus);
                    if (plus > score) (value, score) = (original + steps[k], plus);
                    p[k] = value;
                    best = score;
                }

                for (var k = 0; k < steps.Length; k++)
                {
                    steps[k] /= 2;
                }
            }

            return (p[0], p[1], p[2], p[3], best);
        }

        private double ScoreAt(GrayFrame frame, double[] p)
        {
            var cos = Math.Cos(p[3]) * p[2];
            var sin = Math.Sin(p[3]) * p[2];
            double sumF = 0, sumF2 = 0, sumTF = 0;
            for (var i = 0; i < _values.Length; i++)
            {
                var f = frame.Sample(p[0] + cos * _dx[i] - sin * _dy[i], p[1] + sin * _dx[i] + cos * _dy[i]);
                sumF += f;
                sumF2 += f * f;
                sumTF += _values[i] * f;
            }

            var variance = sumF2 - sumF * sumF / _values.Length;
            return variance <= 1e-6 ? 0 : sumTF / (_norm * Math.Sqrt(variance));
        }

        /// <summary>How the box should look at the given scale/rotation (same size as the box).</summary>
        private GrayFrame Warp(double scale, double theta)
        {
            var pixels = new byte[_width * _height];
            var cos = Math.Cos(-theta) / scale;
            var sin = Math.Sin(-theta) / scale;
            for (var v = 0; v < _height; v++)
            {
                for (var u = 0; u < _width; u++)
                {
                    var dx = u - (_width - 1) / 2.0;
                    var dy = v - (_height - 1) / 2.0;
                    pixels[v * _width + u] = (byte)Math.Round(_reference.Sample(_referenceX + cos * dx - sin * dy, _referenceY + sin * dx + cos * dy));
                }
            }

            return new GrayFrame(_width, _height, pixels);
        }
    }

    private readonly record struct Match(int Left, int Top, double Score, double SubX, double SubY);

    /// <summary>
    /// Exhaustive NCC search of the template's top-left within ±radius of (predictedLeft, predictedTop).
    /// </summary>
    private static Match MatchAt(GrayFrame frame, Template template, int predictedLeft, int predictedTop, int radius)
    {
        // ponytail: window sums computed per position (3 ops/pixel); integral images would cut that to 1 if big boxes get slow
        var size = radius * 2 + 1;
        var scores = new double[size * size];
        Array.Fill(scores, -1);
        var n = template.Width * template.Height;
        var values = template.Values;
        var pixels = frame.Pixels;
        Parallel.For(0, size, j =>
        {
            var top = predictedTop - radius + j;
            if (top < 0 || top + template.Height > frame.Height)
            {
                return;
            }

            for (var i = 0; i < size; i++)
            {
                var left = predictedLeft - radius + i;
                if (left < 0 || left + template.Width > frame.Width)
                {
                    continue;
                }

                double sumF = 0, sumF2 = 0, sumTF = 0;
                for (var ty = 0; ty < template.Height; ty++)
                {
                    var row = (top + ty) * frame.Width + left;
                    var templateRow = ty * template.Width;
                    for (var tx = 0; tx < template.Width; tx++)
                    {
                        double f = pixels[row + tx];
                        sumF += f;
                        sumF2 += f * f;
                        sumTF += values[templateRow + tx] * f;
                    }
                }

                var variance = sumF2 - sumF * sumF / n;
                scores[j * size + i] = variance <= 1e-6 ? 0 : sumTF / (template.Norm * Math.Sqrt(variance));
            }
        });

        var bestIndex = 0;
        for (var k = 1; k < scores.Length; k++)
        {
            if (scores[k] > scores[bestIndex])
            {
                bestIndex = k;
            }
        }

        var bi = bestIndex % size;
        var bj = bestIndex / size;
        var best = scores[bestIndex];
        var subX = bi > 0 && bi < size - 1 ? Parabola(scores[bestIndex - 1], best, scores[bestIndex + 1]) : 0;
        var subY = bj > 0 && bj < size - 1 ? Parabola(scores[bestIndex - size], best, scores[bestIndex + size]) : 0;
        return new Match(predictedLeft - radius + bi, predictedTop - radius + bj, best, subX, subY);
    }

    private static double Parabola(double left, double center, double right)
    {
        if (left <= -1 || right <= -1)
        {
            return 0;
        }

        var denominator = left - 2 * center + right;
        return Math.Abs(denominator) < 1e-12 ? 0 : Math.Clamp((left - right) / (2 * denominator), -0.5, 0.5);
    }

    private sealed class Template
    {
        public int Width { get; private init; }
        public int Height { get; private init; }
        public required double[] Values { get; init; } // zero-mean
        public double Norm { get; private init; }
        public double StdDev { get; private init; }

        public static Template? From(GrayFrame frame, int left, int top, int width, int height)
        {
            if (width < 2 || height < 2 || left < 0 || top < 0 || left + width > frame.Width || top + height > frame.Height)
            {
                return null;
            }

            var values = new double[width * height];
            double sum = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    double v = frame.Pixels[(top + y) * frame.Width + left + x];
                    values[y * width + x] = v;
                    sum += v;
                }
            }

            var mean = sum / values.Length;
            double sumSquares = 0;
            for (var k = 0; k < values.Length; k++)
            {
                values[k] -= mean;
                sumSquares += values[k] * values[k];
            }

            return new Template
            {
                Width = width,
                Height = height,
                Values = values,
                Norm = Math.Sqrt(sumSquares),
                StdDev = Math.Sqrt(sumSquares / values.Length),
            };
        }
    }
}
