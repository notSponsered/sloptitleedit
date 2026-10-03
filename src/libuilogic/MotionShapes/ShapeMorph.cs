using Nikse.SubtitleEdit.UiLogic.Assa;

namespace Nikse.SubtitleEdit.UiLogic.MotionShapes;

/// <summary>
/// p ↦ Target + Scale·R(Theta)·(p − Center). Y points down, so a positive Theta turns clockwise on screen
/// (the same convention as the motion tracker and MotionApplier).
/// </summary>
public readonly record struct Similarity(double CenterX, double CenterY, double TargetX, double TargetY, double Scale, double Theta)
{
    public static Similarity Identity { get; } = new(0, 0, 0, 0, 1, 0);

    public (double X, double Y) Apply(double x, double y)
    {
        var dx = x - CenterX;
        var dy = y - CenterY;
        var cos = Math.Cos(Theta) * Scale;
        var sin = Math.Sin(Theta) * Scale;
        return (TargetX + cos * dx - sin * dy, TargetY + sin * dx + cos * dy);
    }

    public Similarity Inverse() => new(TargetX, TargetY, CenterX, CenterY, 1 / Scale, -Theta);

    /// <summary>Anchors and handles are transformed - béziers are affine-invariant, so the curves follow exactly.</summary>
    public PenPath Apply(PenPath path)
    {
        var result = new PenPath();
        foreach (var a in path.Anchors)
        {
            var (x, y) = Apply(a.X, a.Y);
            var (inX, inY) = Apply(a.InX, a.InY);
            var (outX, outY) = Apply(a.OutX, a.OutY);
            result.Anchors.Add(new PenAnchor(x, y) { InX = inX, InY = inY, OutX = outX, OutY = outY });
        }

        return result;
    }
}

/// <summary>
/// Shape correspondence and morphing between two keyframe shapes.
/// Same anchor count: anchors and handles are interpolated 1:1 (curves stay curves).
/// Different topology (e.g. circle → square): both outlines are resampled by arc length to N points, wound the same way and
/// started at the best-matching point, then interpolated as polygons (the "flubber" approach).
/// </summary>
public static class ShapeMorph
{
    /// <summary>The closed outline as a polyline (curves sampled with <paramref name="steps"/> points per segment).</summary>
    public static List<(double X, double Y)> Flatten(PenPath path, int steps = 16)
    {
        var points = new List<(double X, double Y)>();
        var anchors = path.Anchors;
        for (var i = 0; i < anchors.Count; i++)
        {
            var from = anchors[i];
            var to = anchors[(i + 1) % anchors.Count];
            points.Add((from.X, from.Y));
            if (IsStraight(from, to))
            {
                continue;
            }

            for (var s = 1; s < steps; s++)
            {
                var t = (double)s / steps;
                var mt = 1 - t;
                var a = mt * mt * mt;
                var b = 3 * mt * mt * t;
                var c = 3 * mt * t * t;
                var d = t * t * t;
                points.Add((a * from.X + b * from.OutX + c * to.InX + d * to.X,
                            a * from.Y + b * from.OutY + c * to.InY + d * to.Y));
            }
        }

        return points;
    }

    private static bool IsStraight(PenAnchor from, PenAnchor to) =>
        from.OutX == from.X && from.OutY == from.Y && to.InX == to.X && to.InY == to.Y;

    /// <summary>Positive = clockwise on screen (y down).</summary>
    public static double SignedArea(IReadOnlyList<(double X, double Y)> poly)
    {
        double area = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var (x1, y1) = poly[i];
            var (x2, y2) = poly[(i + 1) % poly.Count];
            area += x1 * y2 - x2 * y1;
        }

        return area / 2;
    }

    public static double Perimeter(IReadOnlyList<(double X, double Y)> poly)
    {
        double length = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            length += Distance(poly[i], poly[(i + 1) % poly.Count]);
        }

        return length;
    }

    public static (double X, double Y) Centroid(IReadOnlyList<(double X, double Y)> points) =>
        points.Count == 0 ? (0, 0) : (points.Average(p => p.X), points.Average(p => p.Y));

    /// <summary>
    /// <paramref name="n"/> points spaced evenly along the closed outline (closing edge included). Each corner is then moved
    /// onto its nearest sample, so e.g. a square keeps its four corners.
    /// </summary>
    public static List<(double X, double Y)> Resample(IReadOnlyList<(double X, double Y)> poly, int n, IEnumerable<(double X, double Y)>? corners = null)
    {
        var result = new List<(double X, double Y)>(n);
        var total = Perimeter(poly);
        if (poly.Count == 0 || total <= 0)
        {
            for (var k = 0; k < n; k++)
            {
                result.Add(poly.Count > 0 ? poly[0] : (0, 0));
            }

            return result;
        }

        var step = total / n;
        var edge = 0;
        var edgeStart = 0.0;
        var edgeLength = Distance(poly[0], poly[1 % poly.Count]);
        for (var k = 0; k < n; k++)
        {
            var s = k * step;
            while (edgeStart + edgeLength < s && edge < poly.Count - 1)
            {
                edgeStart += edgeLength;
                edge++;
                edgeLength = Distance(poly[edge], poly[(edge + 1) % poly.Count]);
            }

            var a = poly[edge];
            var b = poly[(edge + 1) % poly.Count];
            var t = edgeLength > 0 ? Math.Clamp((s - edgeStart) / edgeLength, 0, 1) : 0;
            result.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
        }

        if (corners != null)
        {
            var taken = new HashSet<int>();
            foreach (var corner in corners)
            {
                var best = -1;
                var bestDistance = double.MaxValue;
                for (var k = 0; k < n; k++)
                {
                    var d = Distance(result[k], corner);
                    if (d < bestDistance && !taken.Contains(k))
                    {
                        bestDistance = d;
                        best = k;
                    }
                }

                if (best >= 0)
                {
                    result[best] = corner;
                    taken.Add(best);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="b"/> reordered to match <paramref name="a"/> point by point: same winding, then the cyclic start with the
    /// smallest summed squared distance (translation doesn't change which start wins; rotation must be removed first).
    /// </summary>
    public static List<(double X, double Y)> Correspond(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b)
    {
        var points = b.ToList();
        if (Math.Sign(SignedArea(a)) != Math.Sign(SignedArea(points)))
        {
            points.Reverse();
        }

        var n = points.Count;
        var bestShift = 0;
        var bestCost = double.MaxValue;
        for (var shift = 0; shift < n; shift++)
        {
            double cost = 0;
            for (var i = 0; i < n && cost < bestCost; i++)
            {
                var p = points[(i + shift) % n];
                cost += (a[i].X - p.X) * (a[i].X - p.X) + (a[i].Y - p.Y) * (a[i].Y - p.Y);
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                bestShift = shift;
            }
        }

        return Enumerable.Range(0, n).Select(i => points[(i + bestShift) % n]).ToList();
    }

    /// <summary>
    /// <paramref name="b"/> (same anchor count) re-indexed to match <paramref name="a"/>: reversed when it winds the other way
    /// (In/Out handles swap) and started at the best-matching anchor.
    /// </summary>
    public static PenPath AlignAnchors(PenPath a, PenPath b) => Reorder(b, Alignment(a, b));

    /// <summary>How <paramref name="b"/> (same anchor count) must be reversed/shifted to match <paramref name="a"/>.</summary>
    public static (bool Reversed, int Shift) Alignment(PenPath a, PenPath b)
    {
        var aPoly = a.Anchors.Select(x => (x.X, x.Y)).ToList();
        var reversed = Math.Sign(SignedArea(aPoly)) != Math.Sign(SignedArea(b.Anchors.Select(x => (x.X, x.Y)).ToList()));
        var anchors = Reorder(b, (reversed, 0)).Anchors;
        var n = anchors.Count;
        var bestShift = 0;
        var bestCost = double.MaxValue;
        for (var shift = 0; shift < n; shift++)
        {
            double cost = 0;
            for (var i = 0; i < n; i++)
            {
                var p = anchors[(i + shift) % n];
                cost += (aPoly[i].X - p.X) * (aPoly[i].X - p.X) + (aPoly[i].Y - p.Y) * (aPoly[i].Y - p.Y);
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                bestShift = shift;
            }
        }

        return (reversed, bestShift);
    }

    /// <summary>A copy of <paramref name="path"/> reversed (In/Out handles swap) and/or started at another anchor.</summary>
    public static PenPath Reorder(PenPath path, (bool Reversed, int Shift) alignment)
    {
        var anchors = path.Anchors.Select(x => x.Clone()).ToList();
        if (alignment.Reversed)
        {
            anchors.Reverse();
            foreach (var anchor in anchors)
            {
                (anchor.InX, anchor.OutX) = (anchor.OutX, anchor.InX);
                (anchor.InY, anchor.OutY) = (anchor.OutY, anchor.InY);
            }
        }

        var n = anchors.Count;
        var result = new PenPath();
        result.Anchors.AddRange(Enumerable.Range(0, n).Select(i => anchors[(i + alignment.Shift) % n]));
        return result;
    }

    /// <summary>Anchor-by-anchor interpolation (same anchor count, already aligned).</summary>
    public static PenPath Lerp(PenPath a, PenPath b, double u)
    {
        var result = new PenPath();
        for (var i = 0; i < a.Anchors.Count; i++)
        {
            var p = a.Anchors[i];
            var q = b.Anchors[i];
            result.Anchors.Add(new PenAnchor(L(p.X, q.X, u), L(p.Y, q.Y, u))
            {
                InX = L(p.InX, q.InX, u),
                InY = L(p.InY, q.InY, u),
                OutX = L(p.OutX, q.OutX, u),
                OutY = L(p.OutY, q.OutY, u),
            });
        }

        return result;
    }

    public static List<(double X, double Y)> Lerp(IReadOnlyList<(double X, double Y)> a, IReadOnlyList<(double X, double Y)> b, double u) =>
        Enumerable.Range(0, a.Count).Select(i => (L(a[i].X, b[i].X, u), L(a[i].Y, b[i].Y, u))).ToList();

    /// <summary>
    /// The morph between two shapes at <paramref name="u"/> (0 = a, 1 = b exactly). Same anchor count keeps the curves;
    /// otherwise a polygon with one point per ~3 px of outline (32-256 points).
    /// </summary>
    public static PenPath Morph(PenPath a, PenPath b, double u) => Morpher(a, b)(u);

    /// <summary><see cref="Morph"/> with the correspondence worked out once, for many <c>u</c> values.</summary>
    public static Func<double, PenPath> Morpher(PenPath a, PenPath b)
    {
        if (a.Anchors.Count == b.Anchors.Count)
        {
            var aligned = AlignAnchors(a, b);
            return u => Lerp(a, aligned, u);
        }

        var flatA = Flatten(a);
        var flatB = Flatten(b);
        var n = Math.Clamp((int)Math.Ceiling(Math.Max(Perimeter(flatA), Perimeter(flatB)) / 3), 32, 256);
        var pa = Resample(flatA, n, Corners(a));
        var pb = Correspond(pa, Resample(flatB, n, Corners(b)));
        return u => Polygon(Lerp(pa, pb, u));
    }

    private static IEnumerable<(double X, double Y)> Corners(PenPath path) =>
        path.Anchors.Where(a => !a.IsSmooth).Select(a => (a.X, a.Y));

    public static PenPath Polygon(IEnumerable<(double X, double Y)> points)
    {
        var path = new PenPath();
        path.Anchors.AddRange(points.Select(p => new PenAnchor(p.X, p.Y)));
        return path;
    }

    /// <summary>Closed Douglas-Peucker: drops points closer than <paramref name="epsilon"/> to the simplified outline.</summary>
    public static List<(double X, double Y)> Simplify(IReadOnlyList<(double X, double Y)> poly, double epsilon)
    {
        if (poly.Count <= 3)
        {
            return poly.ToList();
        }

        // split the ring at point 0 and the point farthest from it, simplify both halves
        var far = 0;
        var farDistance = -1.0;
        for (var i = 1; i < poly.Count; i++)
        {
            var d = Distance(poly[0], poly[i]);
            if (d > farDistance)
            {
                farDistance = d;
                far = i;
            }
        }

        var keep = new bool[poly.Count];
        keep[0] = keep[far] = true;
        SimplifyRange(poly, 0, far, epsilon, keep);
        SimplifyRange(poly, far, poly.Count, epsilon, keep);
        var result = poly.Where((_, i) => keep[i]).ToList();
        return result.Count >= 3 ? result : poly.ToList();
    }

    private static void SimplifyRange(IReadOnlyList<(double X, double Y)> poly, int first, int last, double epsilon, bool[] keep)
    {
        // last may equal Count: the end point is poly[0] (closing edge)
        var a = poly[first];
        var b = poly[last % poly.Count];
        var index = -1;
        var max = epsilon;
        for (var i = first + 1; i < last; i++)
        {
            var d = DistanceToSegment(poly[i], a, b);
            if (d > max)
            {
                max = d;
                index = i;
            }
        }

        if (index < 0)
        {
            return;
        }

        keep[index] = true;
        SimplifyRange(poly, first, index, epsilon, keep);
        SimplifyRange(poly, index, last, epsilon, keep);
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0)
        {
            return Distance(p, a);
        }

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, (a.X + t * dx, a.Y + t * dy));
    }

    public static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double L(double a, double b, double u) => a + (b - a) * u;
}
