using Nikse.SubtitleEdit.UiLogic.Assa;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>
/// Fewer drawing points for the same outline: the shape is refitted with as few bézier curves as stay within a tolerance
/// (Schneider, "An Algorithm for Automatically Fitting Digitized Curves", Graphics Gems 1990), keeping sharp corners;
/// a straight-sided shape becomes a Douglas-Peucker polygon instead when that is fewer points.
/// </summary>
public static class ShapeSimplify
{
    /// <summary>A vertex turning more than this (degrees) is a corner the fit keeps.</summary>
    private const double CornerAngle = 40;

    /// <summary>ASSA drawing points (m/l points, or 1 + 3 per curve) the path is written with.</summary>
    public static int PointCount(PenPath path) => path.ToPoints().Count;

    /// <summary>The path with the fewest drawing points within <paramref name="tolerance"/> (path units); 0 = unchanged.</summary>
    public static PenPath Simplify(PenPath path, double tolerance)
    {
        if (tolerance <= 0 || path.Anchors.Count < 3)
        {
            return path;
        }

        var outline = ShapeMorph.Flatten(path);
        var perimeter = ShapeMorph.Perimeter(outline);
        if (perimeter <= 0)
        {
            return path;
        }

        var corners = Corners(path);
        var best = path;
        var polygon = ShapeMorph.Polygon(ShapeMorph.Simplify(ShapeMorph.Resample(outline, Math.Clamp((int)(perimeter / 2), 16, 2000), corners), tolerance));
        if (PointCount(polygon) < PointCount(best))
        {
            best = polygon;
        }

        var curves = FitCurves(outline, perimeter, corners, tolerance);
        if (curves != null && PointCount(curves) < PointCount(best))
        {
            best = curves;
        }

        return best;
    }

    /// <summary>Anchors where the outline turns sharply (tangent in vs tangent out).</summary>
    private static List<(double X, double Y)> Corners(PenPath path)
    {
        var result = new List<(double X, double Y)>();
        var anchors = path.Anchors;
        for (var i = 0; i < anchors.Count; i++)
        {
            var a = anchors[i];
            var previous = anchors[(i - 1 + anchors.Count) % anchors.Count];
            var next = anchors[(i + 1) % anchors.Count];

            // direction coming in = from the in-handle (or the previous point) to the anchor, going out = to the out-handle (or next)
            var (inX, inY) = a.InX != a.X || a.InY != a.Y ? (a.X - a.InX, a.Y - a.InY) : (a.X - previous.OutX, a.Y - previous.OutY);
            var (outX, outY) = a.OutX != a.X || a.OutY != a.Y ? (a.OutX - a.X, a.OutY - a.Y) : (next.InX - a.X, next.InY - a.Y);
            var lengths = Math.Sqrt(inX * inX + inY * inY) * Math.Sqrt(outX * outX + outY * outY);
            if (lengths < 1e-9)
            {
                continue;
            }

            var cos = Math.Clamp((inX * outX + inY * outY) / lengths, -1, 1);
            if (Math.Acos(cos) * 180 / Math.PI > CornerAngle)
            {
                result.Add((a.X, a.Y));
            }
        }

        return result;
    }

    private static PenPath? FitCurves(List<(double X, double Y)> outline, double perimeter, List<(double X, double Y)> corners, double tolerance)
    {
        var count = Math.Clamp((int)(perimeter / Math.Max(0.5, tolerance)), 24, 1000);
        var points = ShapeMorph.Resample(outline, count, corners);

        // split the closed outline at its corners (or once, smoothly, when it has none)
        var cornerIndices = corners
            .Select(c => points.FindIndex(p => ShapeMorph.Distance(p, c) < 1e-9))
            .Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();
        var segments = new List<((double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2, (double X, double Y) P3)>();
        if (cornerIndices.Count == 0)
        {
            var run = points.Append(points[0]).ToList();
            var tangent = Normalize(Sub(points[1], points[^1]));
            FitCubic(run, tangent, Neg(tangent), tolerance, segments);
        }
        else
        {
            for (var k = 0; k < cornerIndices.Count; k++)
            {
                var start = cornerIndices[k];
                var end = cornerIndices[(k + 1) % cornerIndices.Count];
                var length = (end - start + points.Count) % points.Count;
                if (length == 0)
                {
                    length = points.Count; // a single corner: the whole outline from it back to it
                }

                var run = Enumerable.Range(0, length + 1).Select(j => points[(start + j) % points.Count]).ToList();
                FitCubic(run, Normalize(Sub(run[1], run[0])), Normalize(Sub(run[^2], run[^1])), tolerance, segments);
            }
        }

        if (segments.Count == 0)
        {
            return null;
        }

        var path = new PenPath();
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            var previous = segments[(i - 1 + segments.Count) % segments.Count];
            path.Anchors.Add(new PenAnchor(s.P0.X, s.P0.Y) { InX = previous.C2.X, InY = previous.C2.Y, OutX = s.C1.X, OutY = s.C1.Y });
        }

        return path;
    }

    private static void FitCubic(List<(double X, double Y)> d, (double X, double Y) tHat1, (double X, double Y) tHat2, double error,
        List<((double X, double Y), (double X, double Y), (double X, double Y), (double X, double Y))> output)
    {
        if (d.Count == 2)
        {
            var dist = ShapeMorph.Distance(d[0], d[1]) / 3;
            output.Add((d[0], Add(d[0], Scale(tHat1, dist)), Add(d[1], Scale(tHat2, dist)), d[1]));
            return;
        }

        var u = ChordLengthParameterize(d);
        var bezier = GenerateBezier(d, u, tHat1, tHat2);
        var (maxError, split) = MaxError(d, bezier, u);
        if (maxError < error)
        {
            output.Add(bezier);
            return;
        }

        if (maxError < error * 4)
        {
            for (var i = 0; i < 4; i++)
            {
                u = Reparameterize(d, u, bezier);
                bezier = GenerateBezier(d, u, tHat1, tHat2);
                (maxError, split) = MaxError(d, bezier, u);
                if (maxError < error)
                {
                    output.Add(bezier);
                    return;
                }
            }
        }

        // split at the worst point, smooth (same tangent both sides) there
        split = Math.Clamp(split, 1, d.Count - 2);
        var tHatCenter = Normalize(Sub(d[split - 1], d[split + 1]));
        FitCubic(d.GetRange(0, split + 1), tHat1, tHatCenter, error, output);
        FitCubic(d.GetRange(split, d.Count - split), Neg(tHatCenter), tHat2, error, output);
    }

    private static ((double X, double Y), (double X, double Y), (double X, double Y), (double X, double Y)) GenerateBezier(
        List<(double X, double Y)> d, double[] u, (double X, double Y) tHat1, (double X, double Y) tHat2)
    {
        var first = d[0];
        var last = d[^1];
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        for (var i = 0; i < d.Count; i++)
        {
            var t = u[i];
            var mt = 1 - t;
            var b0 = mt * mt * mt;
            var b1 = 3 * t * mt * mt;
            var b2 = 3 * t * t * mt;
            var b3 = t * t * t;
            var a1 = Scale(tHat1, b1);
            var a2 = Scale(tHat2, b2);
            c00 += Dot(a1, a1);
            c01 += Dot(a1, a2);
            c11 += Dot(a2, a2);
            var tmp = Sub(d[i], Add(Scale(first, b0 + b1), Scale(last, b2 + b3)));
            x0 += Dot(a1, tmp);
            x1 += Dot(a2, tmp);
        }

        var det = c00 * c11 - c01 * c01;
        var alpha1 = Math.Abs(det) > 1e-12 ? (x0 * c11 - x1 * c01) / det : 0;
        var alpha2 = Math.Abs(det) > 1e-12 ? (c00 * x1 - c01 * x0) / det : 0;
        var segmentLength = ShapeMorph.Distance(first, last);
        var epsilon = 1e-6 * segmentLength;
        if (alpha1 < epsilon || alpha2 < epsilon)
        {
            alpha1 = alpha2 = segmentLength / 3; // fall back to the Wu/Barsky heuristic
        }

        return (first, Add(first, Scale(tHat1, alpha1)), Add(last, Scale(tHat2, alpha2)), last);
    }

    private static (double Error, int Split) MaxError(List<(double X, double Y)> d,
        ((double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2, (double X, double Y) P3) bezier, double[] u)
    {
        var maxDistance = 0.0;
        var split = d.Count / 2;
        for (var i = 1; i < d.Count - 1; i++)
        {
            var distance = ShapeMorph.Distance(Point(bezier, u[i]), d[i]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                split = i;
            }
        }

        return (maxDistance, split);
    }

    private static double[] Reparameterize(List<(double X, double Y)> d, double[] u,
        ((double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2, (double X, double Y) P3) b)
    {
        var result = new double[u.Length];
        for (var i = 0; i < u.Length; i++)
        {
            // one Newton-Raphson step towards the nearest point on the curve
            var t = u[i];
            var q = Point(b, t);
            var q1 = Derivative(b, t);
            var q2 = SecondDerivative(b, t);
            var diff = Sub(q, d[i]);
            var numerator = Dot(diff, q1);
            var denominator = Dot(q1, q1) + Dot(diff, q2);
            result[i] = Math.Abs(denominator) > 1e-12 ? Math.Clamp(t - numerator / denominator, 0, 1) : t;
        }

        return result;
    }

    private static double[] ChordLengthParameterize(List<(double X, double Y)> d)
    {
        var u = new double[d.Count];
        for (var i = 1; i < d.Count; i++)
        {
            u[i] = u[i - 1] + ShapeMorph.Distance(d[i], d[i - 1]);
        }

        var total = u[^1];
        for (var i = 1; i < d.Count; i++)
        {
            u[i] = total > 0 ? u[i] / total : (double)i / (d.Count - 1);
        }

        return u;
    }

    private static (double X, double Y) Point(((double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2, (double X, double Y) P3) b, double t)
    {
        var mt = 1 - t;
        return Add(Add(Scale(b.P0, mt * mt * mt), Scale(b.C1, 3 * mt * mt * t)), Add(Scale(b.C2, 3 * mt * t * t), Scale(b.P3, t * t * t)));
    }

    private static (double X, double Y) Derivative(((double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2, (double X, double Y) P3) b, double t)
    {
        var mt = 1 - t;
        return Add(Add(Scale(Sub(b.C1, b.P0), 3 * mt * mt), Scale(Sub(b.C2, b.C1), 6 * mt * t)), Scale(Sub(b.P3, b.C2), 3 * t * t));
    }

    private static (double X, double Y) SecondDerivative(((double X, double Y) P0, (double X, double Y) C1, (double X, double Y) C2, (double X, double Y) P3) b, double t) =>
        Add(Scale(Add(Sub(b.C2, Scale(b.C1, 2)), b.P0), 6 * (1 - t)), Scale(Add(Sub(b.P3, Scale(b.C2, 2)), b.C1), 6 * t));

    private static (double X, double Y) Add((double X, double Y) a, (double X, double Y) b) => (a.X + b.X, a.Y + b.Y);

    private static (double X, double Y) Sub((double X, double Y) a, (double X, double Y) b) => (a.X - b.X, a.Y - b.Y);

    private static (double X, double Y) Scale((double X, double Y) a, double s) => (a.X * s, a.Y * s);

    private static (double X, double Y) Neg((double X, double Y) a) => (-a.X, -a.Y);

    private static double Dot((double X, double Y) a, (double X, double Y) b) => a.X * b.X + a.Y * b.Y;

    private static (double X, double Y) Normalize((double X, double Y) a)
    {
        var length = Math.Sqrt(a.X * a.X + a.Y * a.Y);
        return length > 1e-12 ? (a.X / length, a.Y / length) : (1, 0);
    }
}
