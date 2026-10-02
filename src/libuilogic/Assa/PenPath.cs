namespace Nikse.SubtitleEdit.UiLogic.Assa;

/// <summary>
/// A point of a pen-tool path. A corner point has its handles on the point itself; a smooth (curved) point has
/// an incoming and an outgoing handle (bézier control points), like the pen tool in Figma / Inkscape / Illustrator.
/// </summary>
public sealed class PenAnchor(double x, double y)
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
    public double InX { get; set; } = x;
    public double InY { get; set; } = y;
    public double OutX { get; set; } = x;
    public double OutY { get; set; } = y;

    public bool IsSmooth => InX != X || InY != Y || OutX != X || OutY != Y;

    public void MoveTo(double x, double y)
    {
        var (dx, dy) = (x - X, y - Y);
        X = x;
        Y = y;
        InX += dx;
        InY += dy;
        OutX += dx;
        OutY += dy;
    }

    /// <summary>Sets the outgoing handle and mirrors the incoming one (symmetric smooth point).</summary>
    public void SetOutMirrored(double x, double y)
    {
        OutX = x;
        OutY = y;
        InX = 2 * X - x;
        InY = 2 * Y - y;
    }

    public void MakeCorner()
    {
        InX = OutX = X;
        InY = OutY = Y;
    }

    public PenAnchor Clone() => new(X, Y) { InX = InX, InY = InY, OutX = OutX, OutY = OutY };
}

/// <summary>Kind of an ASSA drawing point (maps to "m", "l" and the three "b" values).</summary>
public enum PenPointKind
{
    Start,
    Line,
    Control1,
    Control2,
    CurveEnd,
}

public readonly record struct PenPoint(PenPointKind Kind, double X, double Y);

/// <summary>
/// A closed (or still open) pen-tool path and its conversion to/from ASSA drawing points.
/// ASSA closes a shape with a straight line, so a curved closing segment is written out explicitly.
/// A shape with any curve is written all as "b" (straight segments get handles on the line, which draws straight).
/// </summary>
public sealed class PenPath
{
    public List<PenAnchor> Anchors { get; } = [];

    public bool HasCurves => Anchors.Any(a => a.IsSmooth);

    public List<PenPoint> ToPoints()
    {
        var result = new List<PenPoint>();
        if (Anchors.Count == 0)
        {
            return result;
        }

        result.Add(new PenPoint(PenPointKind.Start, Anchors[0].X, Anchors[0].Y));
        if (!HasCurves)
        {
            result.AddRange(Anchors.Skip(1).Select(a => new PenPoint(PenPointKind.Line, a.X, a.Y)));
            return result;
        }

        for (var i = 1; i <= Anchors.Count; i++)
        {
            var from = Anchors[i - 1];
            var to = Anchors[i % Anchors.Count];
            if (i == Anchors.Count && from.OutX == from.X && from.OutY == from.Y && to.InX == to.X && to.InY == to.Y)
            {
                break; // straight closing segment - ASSA closes the shape by itself
            }

            if (from.OutX == from.X && from.OutY == from.Y && to.InX == to.X && to.InY == to.Y)
            {
                // straight segment inside a curved shape
                result.Add(new PenPoint(PenPointKind.Control1, from.X + (to.X - from.X) / 3, from.Y + (to.Y - from.Y) / 3));
                result.Add(new PenPoint(PenPointKind.Control2, from.X + (to.X - from.X) * 2 / 3, from.Y + (to.Y - from.Y) * 2 / 3));
            }
            else
            {
                result.Add(new PenPoint(PenPointKind.Control1, from.OutX, from.OutY));
                result.Add(new PenPoint(PenPointKind.Control2, to.InX, to.InY));
            }

            result.Add(new PenPoint(PenPointKind.CurveEnd, to.X, to.Y));
        }

        return result;
    }

    /// <summary>Rebuilds anchors from drawing points ("m" + "l"s, or "m" + "b" triplets) so existing drawings can be edited.</summary>
    public static PenPath FromPoints(IReadOnlyList<PenPoint> points)
    {
        var path = new PenPath();
        if (points.Count == 0)
        {
            return path;
        }

        path.Anchors.Add(new PenAnchor(points[0].X, points[0].Y));
        var i = 1;
        while (i < points.Count)
        {
            var p = points[i];
            if (p.Kind == PenPointKind.Control1 && i + 2 < points.Count)
            {
                var c1 = p;
                var c2 = points[i + 1];
                var end = points[i + 2];
                var from = path.Anchors[^1];
                var straight = Near(c1.X, from.X + (end.X - from.X) / 3) && Near(c1.Y, from.Y + (end.Y - from.Y) / 3) &&
                               Near(c2.X, from.X + (end.X - from.X) * 2 / 3) && Near(c2.Y, from.Y + (end.Y - from.Y) * 2 / 3);
                var first = path.Anchors[0];
                var closesShape = i + 3 >= points.Count && path.Anchors.Count > 1 && Near(end.X, first.X) && Near(end.Y, first.Y);
                var to = closesShape ? first : new PenAnchor(end.X, end.Y);
                if (!straight)
                {
                    from.OutX = c1.X;
                    from.OutY = c1.Y;
                    to.InX = c2.X;
                    to.InY = c2.Y;
                }

                if (!closesShape)
                {
                    path.Anchors.Add(to);
                }

                i += 3;
            }
            else
            {
                path.Anchors.Add(new PenAnchor(p.X, p.Y));
                i++;
            }
        }

        return path;
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.02;

    public PenPath Clone()
    {
        var copy = new PenPath();
        copy.Anchors.AddRange(Anchors.Select(a => a.Clone()));
        return copy;
    }
}
