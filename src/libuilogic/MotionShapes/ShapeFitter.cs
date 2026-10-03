using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using SkiaSharp;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>8-bit RGB image (R, G, B per pixel).</summary>
public sealed class RgbFrame(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;

    public static RgbFrame FromFile(string fileName)
    {
        using var source = SKBitmap.Decode(fileName) ?? throw new InvalidOperationException("Unable to decode " + fileName);
        using var bitmap = source.ColorType == SKColorType.Bgra8888 ? null : source.Copy(SKColorType.Bgra8888);
        var bgra = (bitmap ?? source).GetPixelSpan();
        var rowBytes = (bitmap ?? source).RowBytes;
        var width = source.Width; // native calls - keep them out of the loop
        var height = source.Height;
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var row = bgra.Slice(y * rowBytes, width * 4);
            var offset = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                pixels[offset + x * 3] = row[x * 4 + 2];
                pixels[offset + x * 3 + 1] = row[x * 4 + 1];
                pixels[offset + x * 3 + 2] = row[x * 4];
            }
        }

        return new RgbFrame(width, height, pixels);
    }

    /// <summary>Rec. 601 luma, the same as <see cref="GrayFrame.FromFile"/>.</summary>
    public GrayFrame ToGray()
    {
        var gray = new byte[Width * Height];
        for (var i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)((Pixels[i * 3 + 2] * 29 + Pixels[i * 3 + 1] * 150 + Pixels[i * 3] * 77) >> 8);
        }

        return new GrayFrame(Width, Height, gray);
    }

    /// <summary>Bilinear sample at pixel-index coordinates (clamped to the image), written to rgb[0..2].</summary>
    public void Sample(double x, double y, Span<double> rgb)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var fx = x - x0;
        var fy = y - y0;
        var a = (y0 * Width + x0) * 3;
        var b = (y0 * Width + x1) * 3;
        var c = (y1 * Width + x0) * 3;
        var d = (y1 * Width + x1) * 3;
        for (var ch = 0; ch < 3; ch++)
        {
            var top = Pixels[a + ch] + (Pixels[b + ch] - Pixels[a + ch]) * fx;
            var bottom = Pixels[c + ch] + (Pixels[d + ch] - Pixels[c + ch]) * fx;
            rgb[ch] = top + (bottom - top) * fy;
        }
    }
}

/// <summary>The extracted frames (frame px) for the fitter, with a small cache - forward and backward passes reuse frames.</summary>
public sealed class ShapeVideo(Func<int, RgbFrame> load, int count, double toVideoX, double toVideoY)
{
    private const int CacheSize = 8;
    private readonly Dictionary<int, (RgbFrame Rgb, GrayFrame Gray)> _cache = [];
    private readonly Queue<int> _order = new();

    public int Count => count;

    /// <summary>Video px per frame px.</summary>
    public double ToVideoX => toVideoX;
    public double ToVideoY => toVideoY;

    public RgbFrame Rgb(int index) => Get(index).Rgb;

    public GrayFrame Gray(int index) => Get(index).Gray;

    private (RgbFrame Rgb, GrayFrame Gray) Get(int index)
    {
        lock (_cache) // the tracker loads its next frame on another thread
        {
            if (_cache.TryGetValue(index, out var cached))
            {
                return cached;
            }

            var rgb = load(index);
            var frame = (rgb, rgb.ToGray());
            if (_order.Count >= CacheSize)
            {
                _cache.Remove(_order.Dequeue());
            }

            _cache[index] = frame;
            _order.Enqueue(index);
            return frame;
        }
    }
}

/// <summary>
/// Follows a shape through the video by its outline: the key tells what the edge looks like at every point of the outline
/// (a short colour profile across it), and on each frame every point slides along its normal to where that edge is now.
/// The shape's own anchors and handles are then solved to pass through those points (least squares), so curves stay curves.
/// The tracker first moves the shape by the overall motion (big jumps, shakes); frames identical to the previous one
/// (animation on 2s/3s) keep the shape exactly.
/// </summary>
public static class ShapeFitter
{
    private const int Inside = 10; // profile length into the shape (frame px)
    private const int Outside = 6; // and out of it - less weight, the background can change
    private static readonly int[] SearchRadii = [6, 3]; // local refinement after the affine stage - small, so points can't wander onto other edges

    /// <summary>Mean grey difference (0-255) around the shape below which two frames count as the same drawing.</summary>
    public const double HoldDifference = 0.8; // ponytail: fixed; compression noise on held anime frames is ~0.2-0.3

    /// <summary>A fitted shape (video px) and how well the edges matched (0-1).</summary>
    public readonly record struct Fitted(PenPath Path, double Confidence);

    /// <summary>The shape on frames <paramref name="from"/> (where <paramref name="key"/> is right) to <paramref name="to"/>, either direction.</summary>
    public static List<Fitted> Follow(ShapeVideo video, PenPath key, int from, int to, bool scaleAndRotation, CancellationToken cancellationToken)
    {
        var model = new Model(ShapeLines.Scale(key, 1 / video.ToVideoX, 1 / video.ToVideoY));
        var vars = model.Vars.ToArray();
        var reference = model.Profiles(video.Rgb(from), vars);
        var result = new List<Fitted> { new(key.Clone(), 1) };
        var confidence = 1.0;
        var step = to >= from ? 1 : -1;
        var fittedOn = from; // compared with this frame, not the previous one, so a slow pan adds up and gets refitted
        for (var f = from + step; f != to + step; f += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = model.Bounds(vars);
            if (!IsHeld(video.Gray(fittedOn), video.Gray(f), bounds))
            {
                fittedOn = f;
                if (Motion(video, f - step, f, bounds, scaleAndRotation, cancellationToken) is { } motion)
                {
                    for (var i = 0; i < vars.Length; i++)
                    {
                        vars[i] = motion.Apply(vars[i].X, vars[i].Y);
                    }
                }

                confidence = Fit(model, vars, video.Rgb(f), reference);
            }

            result.Add(new Fitted(ShapeLines.Scale(model.ToPath(vars), video.ToVideoX, video.ToVideoY), confidence));
        }

        return result;
    }

    /// <summary>Same drawing: the frames hardly differ around the shape (frame px).</summary>
    public static bool IsHeld(GrayFrame a, GrayFrame b, TrackBox region)
    {
        var left = Math.Clamp((int)(region.Left - 16), 0, a.Width - 1);
        var top = Math.Clamp((int)(region.Top - 16), 0, a.Height - 1);
        var right = Math.Clamp((int)(region.Left + region.Width + 16), left + 1, a.Width);
        var bottom = Math.Clamp((int)(region.Top + region.Height + 16), top + 1, a.Height);
        long sum = 0;
        var count = 0;
        for (var y = top; y < bottom; y += 2)
        {
            for (var x = left; x < right; x += 2)
            {
                sum += Math.Abs(a.Pixels[y * a.Width + x] - b.Pixels[y * b.Width + x]);
                count++;
            }
        }

        return count > 0 && (double)sum / count < HoldDifference;
    }

    /// <summary>Overall motion from one frame to the next (frame px) by the tracker, or null when it can't tell.</summary>
    private static Similarity? Motion(ShapeVideo video, int from, int to, TrackBox bounds, bool scaleAndRotation, CancellationToken cancellationToken)
    {
        var first = video.Gray(from);
        var box = Clamp(Inflate(bounds, -0.075 * bounds.Width, -0.075 * bounds.Height), first);
        if (box.Width < 8 || box.Height < 8 || !MotionTracker.HasEnoughDetail(first, box))
        {
            box = Clamp(Inflate(bounds, Math.Max(8, 0.1 * bounds.Width), Math.Max(8, 0.1 * bounds.Height)), first); // flat panels: their edges
        }

        var d = Math.Max(1, (int)Math.Ceiling(Math.Max(box.Width, box.Height) / 256));
        var small = first.Downsample(d);
        var smallBox = Clamp(new TrackBox(box.Left / d, box.Top / d, box.Width / d, box.Height / d), small);
        var points = MotionTracker.Track(i => i == from ? small : video.Gray(i).Downsample(d), from, to, [smallBox], scaleAndRotation, cancellationToken);
        if (points.Count < 2)
        {
            return null;
        }

        return new Similarity(points[0].X * d, points[0].Y * d, points[1].X * d, points[1].Y * d, points[1].Scale, points[1].Rotation * Math.PI / 180);
    }

    /// <summary>
    /// Moves <paramref name="vars"/> onto the edges in <paramref name="frame"/>; returns the mean match confidence.
    /// First the whole shape squashes/stretches/moves as one (affine, wide search, robust to points that grab the wrong
    /// edge), then every point is refined locally.
    /// </summary>
    private static double Fit(Model model, (double X, double Y)[] vars, RgbFrame frame, double[][] reference)
    {
        var m = model.SampleCount;
        var n = vars.Length * 2;
        var confidence = 0.0;
        var offsets = new double[m];
        var weights = new double[m];
        var rows = new (int Index, double Value)[m][];

        // a new drawing can be 20-40% taller/wider - search up to a quarter of the shape's size
        var bounds = model.Bounds(vars);
        var wide = (int)Math.Clamp(Math.Min(bounds.Width, bounds.Height) * 0.25, 12, 64);
        foreach (var radius in new[] { wide, Math.Max(8, wide / 2), Math.Max(6, wide / 4) })
        {
            var points = new (double X, double Y, double Nx, double Ny)[m];
            for (var i = 0; i < m; i++)
            {
                points[i] = model.Point(vars, i);
                (offsets[i], weights[i]) = Search(frame, points[i].X, points[i].Y, points[i].Nx, points[i].Ny, reference[i], radius);
            }

            if (RobustAffine(points, offsets, weights) is { } affine)
            {
                for (var j = 0; j < vars.Length; j++)
                {
                    vars[j] = affine(vars[j].X, vars[j].Y);
                }
            }
        }

        foreach (var radius in SearchRadii)
        {
            for (var i = 0; i < m; i++)
            {
                var (x, y, nx, ny) = model.Point(vars, i);
                (offsets[i], weights[i]) = Search(frame, x, y, nx, ny, reference[i], radius);
                rows[i] = model.Row(i, nx, ny);
            }

            offsets = MedianSmooth(offsets); // one bad match must not dent the outline

            // step Δ minimising Σ w_i (a_i·Δ − d_i)² + λ|Δ|²  (a_i·Δ = how far sample i moves along its normal)
            var normal = new double[n, n];
            var rhs = new double[n];
            for (var i = 0; i < m; i++)
            {
                foreach (var (p, ap) in rows[i])
                {
                    rhs[p] += weights[i] * ap * offsets[i];
                    foreach (var (q, aq) in rows[i])
                    {
                        normal[p, q] += weights[i] * ap * aq;
                    }
                }
            }

            double trace = 0;
            for (var p = 0; p < n; p++)
            {
                trace += normal[p, p];
            }

            var lambda = 0.2 * trace / n + 1e-6; // stiff: keeps points without edge evidence (or a lone wrong match) where they are
            for (var p = 0; p < n; p++)
            {
                normal[p, p] += lambda;
            }

            var delta = Solve(normal, rhs);
            for (var j = 0; j < vars.Length; j++)
            {
                vars[j] = (vars[j].X + delta[2 * j], vars[j].Y + delta[2 * j + 1]);
            }

            confidence = weights.Average();
        }

        return confidence;
    }

    /// <summary>Best offset along the normal where the frame's profile matches the key's, and how sure that is (0-1).</summary>
    private static (double Offset, double Confidence) Search(RgbFrame frame, double x, double y, double nx, double ny, double[] reference, int radius)
    {
        var length = Inside + Outside + 1;
        var span = 2 * radius + length;
        var line = new double[span * 3];
        for (var k = 0; k < span; k++)
        {
            var t = k - radius - Inside;
            frame.Sample(x + t * nx, y + t * ny, line.AsSpan(k * 3, 3));
        }

        var costs = new double[2 * radius + 1];
        for (var s = 0; s < costs.Length; s++)
        {
            double cost = 0;
            for (var t = 0; t < length; t++)
            {
                var w = t <= Inside ? 1.0 : 0.5;
                for (var ch = 0; ch < 3; ch++)
                {
                    var diff = line[(s + t) * 3 + ch] - reference[t * 3 + ch];
                    cost += w * diff * diff;
                }
            }

            costs[s] = cost;
        }

        var best = 0;
        for (var s = 1; s < costs.Length; s++)
        {
            if (costs[s] < costs[best])
            {
                best = s;
            }
        }

        var offset = (double)best;
        if (best > 0 && best < costs.Length - 1)
        {
            var denominator = costs[best - 1] - 2 * costs[best] + costs[best + 1];
            if (denominator > 1e-9)
            {
                offset += 0.5 * (costs[best - 1] - costs[best + 1]) / denominator;
            }
        }

        var sorted = costs.OrderBy(c => c).ToArray();
        var median = sorted[sorted.Length / 2];
        var distinct = median > 1e-9 ? Math.Clamp(1 - costs[best] / median, 0, 1) : 0;
        double contrast = 0;
        for (var t = 0; t + 1 < length; t++)
        {
            for (var ch = 0; ch < 3; ch++)
            {
                var g = reference[(t + 1) * 3 + ch] - reference[t * 3 + ch];
                contrast += g * g;
            }
        }

        var edge = Math.Min(1, Math.Sqrt(contrast) / 40); // flat profile = nothing to lock onto
        var atLimit = best == 0 || best == costs.Length - 1 ? 0.2 : 1; // the edge may be further away
        return (offset - radius, distinct * edge * atLimit);
    }

    /// <summary>
    /// The affine move of the whole outline that best explains the per-point edge offsets (point-to-normal), with
    /// iteratively reweighted least squares (Tukey) so the minority of points on a wrong edge is ignored.
    /// </summary>
    private static Func<double, double, (double X, double Y)>? RobustAffine((double X, double Y, double Nx, double Ny)[] points, double[] offsets, double[] weights)
    {
        var m = points.Length;
        var cx = points.Average(p => p.X); // centred for a well-conditioned system
        var cy = points.Average(p => p.Y);
        var robust = Enumerable.Repeat(1.0, m).ToArray();
        double[]? u = null;
        for (var iteration = 0; iteration < 6; iteration++)
        {
            var normal = new double[6, 6];
            var rhs = new double[6];
            var a = new double[6];
            for (var i = 0; i < m; i++)
            {
                var w = weights[i] * robust[i];
                if (w <= 0)
                {
                    continue;
                }

                var (x, y, nx, ny) = points[i];
                (a[0], a[1], a[2], a[3], a[4], a[5]) = (nx * (x - cx), nx * (y - cy), nx, ny * (x - cx), ny * (y - cy), ny);
                for (var p = 0; p < 6; p++)
                {
                    rhs[p] += w * a[p] * offsets[i];
                    for (var q = 0; q < 6; q++)
                    {
                        normal[p, q] += w * a[p] * a[q];
                    }
                }
            }

            double trace = 0;
            for (var p = 0; p < 6; p++)
            {
                trace += normal[p, p];
            }

            if (trace < 1e-6)
            {
                return null; // no edge evidence at all
            }

            for (var p = 0; p < 6; p++)
            {
                normal[p, p] += 1e-4 * normal[p, p] + 1e-9; // relative: the x/y columns are px² larger than the translation ones
            }

            u = Solve(normal, rhs);
            var residuals = new double[m];
            for (var i = 0; i < m; i++)
            {
                var (x, y, nx, ny) = points[i];
                residuals[i] = Math.Abs(nx * (u[0] * (x - cx) + u[1] * (y - cy) + u[2]) + ny * (u[3] * (x - cx) + u[4] * (y - cy) + u[5]) - offsets[i]);
            }

            var scale = 3 * residuals.OrderBy(r => r).ElementAt(m / 2) + 1; // px
            for (var i = 0; i < m; i++)
            {
                var r = residuals[i] / scale;
                robust[i] = r >= 1 ? 0 : (1 - r * r) * (1 - r * r);
            }
        }

        var v = u!;

        // one frame doesn't halve, double or mirror a drawing - that's a bad fit, keep the prediction instead
        var area = (1 + v[0]) * (1 + v[4]) - v[1] * v[3];
        if (double.IsNaN(area) || area < 0.5 || area > 2)
        {
            return null;
        }

        return (x, y) => (x + v[0] * (x - cx) + v[1] * (y - cy) + v[2], y + v[3] * (x - cx) + v[4] * (y - cy) + v[5]);
    }

    private static double[] MedianSmooth(double[] values)
    {
        var n = values.Length;
        var result = new double[n];
        var window = new double[5];
        for (var i = 0; i < n; i++)
        {
            for (var k = -2; k <= 2; k++)
            {
                window[k + 2] = values[((i + k) % n + n) % n];
            }

            Array.Sort(window);
            result[i] = window[2];
        }

        return result;
    }

    /// <summary>Cholesky solve of a symmetric positive definite system.</summary>
    private static double[] Solve(double[,] a, double[] b)
    {
        var n = b.Length;
        var l = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = a[i, j];
                for (var k = 0; k < j; k++)
                {
                    sum -= l[i, k] * l[j, k];
                }

                l[i, j] = i == j ? Math.Sqrt(Math.Max(sum, 1e-12)) : sum / l[j, j];
            }
        }

        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = b[i];
            for (var k = 0; k < i; k++)
            {
                sum -= l[i, k] * y[k];
            }

            y[i] = sum / l[i, i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (var k = i + 1; k < n; k++)
            {
                sum -= l[k, i] * x[k];
            }

            x[i] = sum / l[i, i];
        }

        return x;
    }

    private static TrackBox Inflate(TrackBox box, double dx, double dy) =>
        new(box.Left - dx, box.Top - dy, box.Width + 2 * dx, box.Height + 2 * dy);

    private static TrackBox Clamp(TrackBox box, GrayFrame frame)
    {
        var left = Math.Clamp(Math.Round(box.Left), 0, frame.Width - 1);
        var top = Math.Clamp(Math.Round(box.Top), 0, frame.Height - 1);
        var right = Math.Clamp(Math.Round(box.Left + box.Width), left + 1, frame.Width);
        var bottom = Math.Clamp(Math.Round(box.Top + box.Height), top + 1, frame.Height);
        return new TrackBox(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// A path as free points (anchors, plus handles that differ from their anchor) and fixed samples along it. A sample is a
    /// fixed blend of 4 points (cubic Bernstein, or linear on straight segments), so its position is linear in the points.
    /// </summary>
    private sealed class Model
    {
        private readonly record struct Sample(int P0, int P1, int P2, int P3, double T, bool Straight);

        private readonly (int Anchor, int In, int Out)[] _anchors;
        private readonly List<Sample> _samples = [];
        private readonly double _outward; // +1 clockwise on screen, -1 counter-clockwise

        public List<(double X, double Y)> Vars { get; } = [];

        public int SampleCount => _samples.Count;

        public Model(PenPath path)
        {
            _anchors = path.Anchors.Select(a =>
            {
                var anchor = Add(a.X, a.Y);
                var handleIn = a.InX == a.X && a.InY == a.Y ? anchor : Add(a.InX, a.InY);
                var handleOut = a.OutX == a.X && a.OutY == a.Y ? anchor : Add(a.OutX, a.OutY);
                return (anchor, handleIn, handleOut);
            }).ToArray();

            var flat = ShapeMorph.Flatten(path);
            _outward = ShapeMorph.SignedArea(flat) >= 0 ? 1 : -1;
            var spacing = Math.Clamp(ShapeMorph.Perimeter(flat) / 240, 2, 8);
            for (var k = 0; k < _anchors.Length; k++)
            {
                var from = _anchors[k];
                var to = _anchors[(k + 1) % _anchors.Length];
                var straight = from.Out == from.Anchor && to.In == to.Anchor;
                double length = 0;
                var previous = Position(new Sample(from.Anchor, from.Out, to.In, to.Anchor, 0, straight), Vars);
                for (var s = 1; s <= 8; s++)
                {
                    var p = Position(new Sample(from.Anchor, from.Out, to.In, to.Anchor, s / 8.0, straight), Vars);
                    length += ShapeMorph.Distance(previous, p);
                    previous = p;
                }

                var count = Math.Max(2, (int)Math.Round(length / spacing));
                for (var j = 0; j < count; j++)
                {
                    _samples.Add(new Sample(from.Anchor, from.Out, to.In, to.Anchor, (double)j / count, straight));
                }
            }
        }

        private int Add(double x, double y)
        {
            Vars.Add((x, y));
            return Vars.Count - 1;
        }

        private static (double W0, double W1, double W2, double W3) Weights(Sample s)
        {
            var t = s.T;
            var mt = 1 - t;
            return s.Straight ? (mt, 0, 0, t) : (mt * mt * mt, 3 * mt * mt * t, 3 * mt * t * t, t * t * t);
        }

        private static (double X, double Y) Position(Sample s, IReadOnlyList<(double X, double Y)> v)
        {
            var (w0, w1, w2, w3) = Weights(s);
            return (w0 * v[s.P0].X + w1 * v[s.P1].X + w2 * v[s.P2].X + w3 * v[s.P3].X,
                    w0 * v[s.P0].Y + w1 * v[s.P1].Y + w2 * v[s.P2].Y + w3 * v[s.P3].Y);
        }

        /// <summary>Sample position and outward unit normal.</summary>
        public (double X, double Y, double Nx, double Ny) Point(IReadOnlyList<(double X, double Y)> v, int index)
        {
            var s = _samples[index];
            var (x, y) = Position(s, v);
            double tx, ty;
            if (s.Straight)
            {
                (tx, ty) = (v[s.P3].X - v[s.P0].X, v[s.P3].Y - v[s.P0].Y);
            }
            else
            {
                var t = s.T;
                var mt = 1 - t;
                tx = 3 * (mt * mt * (v[s.P1].X - v[s.P0].X) + 2 * mt * t * (v[s.P2].X - v[s.P1].X) + t * t * (v[s.P3].X - v[s.P2].X));
                ty = 3 * (mt * mt * (v[s.P1].Y - v[s.P0].Y) + 2 * mt * t * (v[s.P2].Y - v[s.P1].Y) + t * t * (v[s.P3].Y - v[s.P2].Y));
                if (tx * tx + ty * ty < 1e-9)
                {
                    (tx, ty) = (v[s.P3].X - v[s.P0].X, v[s.P3].Y - v[s.P0].Y); // handle on the anchor
                }
            }

            var length = Math.Sqrt(tx * tx + ty * ty);
            if (length < 1e-9)
            {
                return (x, y, 0, 0);
            }

            // y points down: for a clockwise outline the outside is to the left of the direction of travel
            return (x, y, _outward * ty / length, -_outward * tx / length);
        }

        /// <summary>How sample <paramref name="index"/> moves along (nx, ny) per unit move of each point coordinate.</summary>
        public (int Index, double Value)[] Row(int index, double nx, double ny)
        {
            var s = _samples[index];
            var (w0, w1, w2, w3) = Weights(s);
            var byVar = new Dictionary<int, double>();
            void Add(int v, double w)
            {
                if (w != 0)
                {
                    byVar[v] = byVar.GetValueOrDefault(v) + w;
                }
            }

            Add(s.P0, w0);
            Add(s.P1, w1);
            Add(s.P2, w2);
            Add(s.P3, w3);
            return byVar.SelectMany(kv => new[] { (2 * kv.Key, kv.Value * nx), (2 * kv.Key + 1, kv.Value * ny) }).ToArray();
        }

        /// <summary>The colour profile across the outline at every sample: <see cref="Inside"/> px in to <see cref="Outside"/> px out.</summary>
        public double[][] Profiles(RgbFrame frame, IReadOnlyList<(double X, double Y)> v)
        {
            var result = new double[_samples.Count][];
            for (var i = 0; i < _samples.Count; i++)
            {
                var (x, y, nx, ny) = Point(v, i);
                var profile = new double[(Inside + Outside + 1) * 3];
                for (var t = 0; t <= Inside + Outside; t++)
                {
                    var d = t - Inside;
                    frame.Sample(x + d * nx, y + d * ny, profile.AsSpan(t * 3, 3));
                }

                result[i] = profile;
            }

            return result;
        }

        public TrackBox Bounds(IReadOnlyList<(double X, double Y)> v)
        {
            var points = Enumerable.Range(0, _samples.Count).Select(i => Position(_samples[i], v)).ToList();
            var left = points.Min(p => p.X);
            var top = points.Min(p => p.Y);
            return new TrackBox(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
        }

        public PenPath ToPath(IReadOnlyList<(double X, double Y)> v)
        {
            var path = new PenPath();
            foreach (var (anchor, handleIn, handleOut) in _anchors)
            {
                path.Anchors.Add(new PenAnchor(v[anchor].X, v[anchor].Y)
                {
                    InX = v[handleIn].X,
                    InY = v[handleIn].Y,
                    OutX = v[handleOut].X,
                    OutY = v[handleOut].Y,
                });
            }

            return path;
        }
    }
}
