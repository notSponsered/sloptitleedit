using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionShapes;

namespace LibUiLogicTests.MotionShapes;

public class ShapeSimplifyTests
{
    private static PenPath CirclePolygon(double cx, double cy, double r, int points) =>
        ShapeMorph.Polygon(Enumerable.Range(0, points).Select(i => (cx + r * Math.Cos(i * 2 * Math.PI / points), cy + r * Math.Sin(i * 2 * Math.PI / points))));

    /// <summary>Largest distance of the simplified outline from the circle.</summary>
    private static double CircleError(PenPath path, double cx, double cy, double r) =>
        ShapeMorph.Flatten(path, 32).Max(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - r));

    [Fact]
    public void DenseRoundPolygon_BecomesFewCurves()
    {
        var circle = CirclePolygon(200, 200, 100, 72); // like a hand-clicked or resampled outline: 72 points

        var simple = ShapeSimplify.Simplify(circle, 1);

        Assert.True(ShapeSimplify.PointCount(simple) <= 25, $"{ShapeSimplify.PointCount(simple)} points");
        Assert.True(simple.HasCurves);
        Assert.True(CircleError(simple, 200, 200, 100) < 1.5, $"{CircleError(simple, 200, 200, 100):0.00} px off");
    }

    [Fact]
    public void Square_KeepsItsFourCorners()
    {
        var square = ShapeMorph.Polygon([(0, 0), (100, 0), (100, 100), (0, 100)]);

        var simple = ShapeSimplify.Simplify(square, 1);

        Assert.Equal(4, ShapeSimplify.PointCount(simple));
    }

    [Fact]
    public void ManyPointsOnStraightSides_BecomePolygon()
    {
        var points = new List<(double X, double Y)>();
        for (var i = 0; i < 10; i++) points.Add((i * 10, 0));
        for (var i = 0; i < 10; i++) points.Add((100, i * 10));
        for (var i = 0; i < 10; i++) points.Add((100 - i * 10, 100));
        for (var i = 0; i < 10; i++) points.Add((0, 100 - i * 10));

        var simple = ShapeSimplify.Simplify(ShapeMorph.Polygon(points), 1);

        Assert.Equal(4, ShapeSimplify.PointCount(simple));
        Assert.False(simple.HasCurves);
    }

    [Fact]
    public void ToleranceZero_Unchanged()
    {
        var circle = CirclePolygon(0, 0, 50, 40);

        Assert.Same(circle, ShapeSimplify.Simplify(circle, 0));
    }

    [Fact]
    public void Write_UsesFewerPoints()
    {
        const string header = "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n";
        var frames = new List<ShapeFrame> { new() { Index = 0, Path = CirclePolygon(500, 500, 150, 90) } };

        var exact = ShapeLines.Write(frames, [0], [new ShapeSegment(0, 0)], 0, 500, header, "Default", 1920, 1080, string.Empty, false, 0);
        var simple = ShapeLines.Write(frames, [0], [new ShapeSegment(0, 0)], 0, 500, header, "Default", 1920, 1080, string.Empty, false);

        Assert.True(simple[0].Text.Length < exact[0].Text.Length / 2);
    }
}
