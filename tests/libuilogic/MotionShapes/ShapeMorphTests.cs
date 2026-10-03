using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionShapes;

namespace LibUiLogicTests.MotionShapes;

public class ShapeMorphTests
{
    private static List<(double X, double Y)> Square(double left, double top, double size) =>
        [(left, top), (left + size, top), (left + size, top + size), (left, top + size)];

    private static PenPath Circle(double cx, double cy, double r)
    {
        // 4-anchor bézier circle
        const double k = 0.5523;
        var path = new PenPath();
        foreach (var (dx, dy) in new[] { (0.0, -1.0), (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0) })
        {
            var anchor = new PenAnchor(cx + dx * r, cy + dy * r);
            // clockwise tangent (y down): (-dy, dx)
            anchor.SetOutMirrored(anchor.X - dy * r * k, anchor.Y + dx * r * k);
            path.Anchors.Add(anchor);
        }

        return path;
    }

    [Fact]
    public void Correspond_UndoesShiftAndReversal()
    {
        var a = ShapeMorph.Resample(Square(0, 0, 40), 16, Square(0, 0, 40));
        var b = a.ToList();
        b.Reverse();
        b = b.Skip(5).Concat(b.Take(5)).ToList();

        var matched = ShapeMorph.Correspond(a, b);

        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(0, ShapeMorph.Distance(a[i], matched[i]), 9);
        }
    }

    [Fact]
    public void Resample_KeepsSquareCorners()
    {
        var square = Square(10, 10, 50);

        var points = ShapeMorph.Resample(square, 37, square);

        Assert.Equal(37, points.Count);
        foreach (var corner in square)
        {
            Assert.Contains(corner, points);
        }
    }

    [Fact]
    public void Morph_CircleToSquare_EndsAreExact()
    {
        var circle = Circle(100, 100, 30);
        // 5 anchors (extra point mid top edge) vs the circle's 4 forces the resampling path
        var square = ShapeMorph.Polygon(Square(200, 70, 60).Take(1).Append((230, 70)).Concat(Square(200, 70, 60).Skip(1)));

        var start = ShapeMorph.Morph(circle, square, 0);
        var end = ShapeMorph.Morph(circle, square, 1);
        var middle = ShapeMorph.Morph(circle, square, 0.5);

        // start lies on the circle, end on the square, same point count everywhere
        Assert.All(start.Anchors, p => Assert.Equal(30, Math.Sqrt((p.X - 100) * (p.X - 100) + (p.Y - 100) * (p.Y - 100)), 0));
        Assert.All(end.Anchors, p => Assert.True(
            (Math.Abs(p.X - 200) < 1e-9 || Math.Abs(p.X - 260) < 1e-9 || Math.Abs(p.Y - 70) < 1e-9 || Math.Abs(p.Y - 130) < 1e-9) &&
            p.X is >= 200 - 1e-9 and <= 260 + 1e-9 && p.Y is >= 70 - 1e-9 and <= 130 + 1e-9));
        Assert.Equal(start.Anchors.Count, end.Anchors.Count);
        Assert.Equal(start.Anchors.Count, middle.Anchors.Count);
        foreach (var corner in Square(200, 70, 60))
        {
            Assert.Contains(end.Anchors, p => Math.Abs(p.X - corner.X) < 1e-9 && Math.Abs(p.Y - corner.Y) < 1e-9);
        }

        // halfway centroid sits halfway between the two centres
        var c = ShapeMorph.Centroid(middle.Anchors.Select(p => (p.X, p.Y)).ToList());
        Assert.Equal(165, c.X, 0);
        Assert.Equal(100, c.Y, 0);
    }

    [Fact]
    public void Morph_SameAnchorCount_KeepsCurves()
    {
        var a = Circle(0, 0, 10);
        var b = Circle(100, 0, 20);

        var middle = ShapeMorph.Morph(a, b, 0.5);

        Assert.Equal(4, middle.Anchors.Count);
        Assert.True(middle.HasCurves);
        Assert.Equal(50, middle.Anchors[0].X, 9);
        Assert.Equal(-15, middle.Anchors[0].Y, 9);
    }

    [Fact]
    public void AlignAnchors_ReversedPath_SwapsHandles()
    {
        var a = Circle(0, 0, 10);
        var b = new PenPath();
        // the same outline drawn the other way round: reversed order, In/Out handles swapped
        b.Anchors.AddRange(a.Anchors.Select(x => new PenAnchor(x.X, x.Y) { InX = x.OutX, InY = x.OutY, OutX = x.InX, OutY = x.InY }).Reverse());

        var aligned = ShapeMorph.AlignAnchors(a, b);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(a.Anchors[i].X, aligned.Anchors[i].X, 9);
            Assert.Equal(a.Anchors[i].OutX, aligned.Anchors[i].OutX, 9);
            Assert.Equal(a.Anchors[i].InY, aligned.Anchors[i].InY, 9);
        }
    }

    [Fact]
    public void Simplify_DenseSquare_FourPoints()
    {
        var square = Square(0, 0, 100);
        var dense = ShapeMorph.Resample(square, 64, square);

        var simple = ShapeMorph.Simplify(dense, 0.25);

        Assert.Equal(4, simple.Count);
        foreach (var corner in square)
        {
            Assert.Contains(corner, simple);
        }
    }

    [Fact]
    public void Similarity_InverseRoundTrips()
    {
        var sim = new Similarity(10, 20, 300, 150, 1.5, 0.3);

        var (x, y) = sim.Apply(17, -4);
        var (bx, by) = sim.Inverse().Apply(x, y);

        Assert.Equal(17, bx, 9);
        Assert.Equal(-4, by, 9);
        Assert.Equal((300.0, 150.0), sim.Apply(10, 20));
    }
}
