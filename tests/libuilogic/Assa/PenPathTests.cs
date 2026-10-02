using Nikse.SubtitleEdit.UiLogic.Assa;

namespace LibUiLogicTests.Assa;

public class PenPathTests
{
    [Fact]
    public void CornersOnly_WritesLines_AndRoundTrips()
    {
        var path = new PenPath();
        path.Anchors.AddRange([new PenAnchor(0, 0), new PenAnchor(10, 0), new PenAnchor(10, 10)]);

        var points = path.ToPoints();

        Assert.Equal([PenPointKind.Start, PenPointKind.Line, PenPointKind.Line], points.Select(p => p.Kind));
        var back = PenPath.FromPoints(points);
        Assert.Equal(3, back.Anchors.Count);
        Assert.False(back.HasCurves);
    }

    [Fact]
    public void CurvedPoint_WritesBezierTriplets_IncludingCurvedClose_AndRoundTrips()
    {
        var path = new PenPath();
        var curved = new PenAnchor(10, 0);
        curved.SetOutMirrored(15, 5);
        path.Anchors.AddRange([new PenAnchor(0, 0), curved, new PenAnchor(10, 10)]);

        var points = path.ToPoints();

        // m + 2 segments (corner->curved, curved->corner); the closing segment is straight so it is left out
        Assert.Equal(7, points.Count);
        Assert.Equal(new PenPoint(PenPointKind.Control2, 5, -5), points[2]); // incoming handle mirrored
        Assert.Equal(new PenPoint(PenPointKind.Control1, 15, 5), points[4]);

        var back = PenPath.FromPoints(points);
        Assert.Equal(3, back.Anchors.Count);
        Assert.True(back.Anchors[1].IsSmooth);
        Assert.False(back.Anchors[0].IsSmooth);
        Assert.False(back.Anchors[2].IsSmooth);
    }

    [Fact]
    public void CurvedFirstPoint_WritesClosingSegment_AndDoesNotDuplicateTheFirstPoint()
    {
        var path = new PenPath();
        var first = new PenAnchor(0, 0);
        first.SetOutMirrored(5, -5);
        path.Anchors.AddRange([first, new PenAnchor(10, 0), new PenAnchor(10, 10)]);

        var points = path.ToPoints();

        Assert.Equal(new PenPoint(PenPointKind.CurveEnd, 0, 0), points[^1]);
        var back = PenPath.FromPoints(points);
        Assert.Equal(3, back.Anchors.Count);
        Assert.True(back.Anchors[0].IsSmooth);
        Assert.Equal(-5, back.Anchors[0].InX, 3);
    }
}
