using Nikse.SubtitleEdit.UiLogic.Assa;

namespace LibUiLogicTests.Assa;

public class AssaDrawingTextTests
{
    [Fact]
    public void Edit_KeepsTags_AndPosOrigin()
    {
        var drawing = AssaDrawingText.Parse("{\\fad(100,0)\\an7\\pos(100,50)\\p1}m 0 0 l 10 0 10 10{\\p0}")!;
        var paths = drawing.ToPaths()!;

        Assert.Single(paths);
        Assert.Equal(3, paths[0].Anchors.Count);
        Assert.Equal(110, paths[0].Anchors[1].X);
        Assert.Equal(50, paths[0].Anchors[1].Y);

        paths[0].Anchors[1].MoveTo(120, 50);
        Assert.Equal("{\\fad(100,0)\\an7\\pos(100,50)\\p1}m 0 0 l 20 0 l 10 10{\\p0}", drawing.WithPaths(paths));
    }

    [Fact]
    public void ScaledDrawing_AndSeveralShapes()
    {
        var drawing = AssaDrawingText.Parse("{\\p2}m 0 0 l 20 0 20 20 m 40 40 l 60 40 60 60")!;
        var paths = drawing.ToPaths()!;

        Assert.Equal(2, paths.Count);
        Assert.Equal(10, paths[0].Anchors[1].X); // \p2 = coordinates are 2x
        Assert.Equal("{\\p2}m 0 0 l 20 0 l 20 20 m 40 40 l 60 40 l 60 60", drawing.WithPaths(paths));
    }

    [Fact]
    public void Curves_RoundTrip()
    {
        var text = "{\\p1}m 0 0 b 5 -5 15 5 20 0 l 20 20{\\p0}";
        var drawing = AssaDrawingText.Parse(text)!;
        var paths = drawing.ToPaths()!;

        Assert.Equal(3, paths[0].Anchors.Count);
        Assert.True(paths[0].Anchors[0].IsSmooth || paths[0].Anchors[1].IsSmooth);
        var again = AssaDrawingText.Parse(drawing.WithPaths(paths))!.ToPaths()!;
        Assert.Equal(paths[0].Anchors.Select(a => (a.X, a.Y)), again[0].Anchors.Select(a => (a.X, a.Y)));
    }

    [Fact]
    public void SplinesAndTextLines_AreNotEditable()
    {
        Assert.Null(AssaDrawingText.Parse("{\\p1}m 0 0 s 10 0 10 10 0 10 c")!.ToPaths());
        Assert.Null(AssaDrawingText.Parse("{\\pos(1,2)}Settings"));
    }

    [Fact]
    public void NewLine_Template()
    {
        var path = new PenPath();
        path.Anchors.AddRange([new PenAnchor(1, 2), new PenAnchor(3, 4), new PenAnchor(5, 6)]);
        Assert.Equal("{\\p1}m 1 2 l 3 4 l 5 6{\\p0}", AssaDrawingText.NewLine.WithPaths([path]));
    }
}
