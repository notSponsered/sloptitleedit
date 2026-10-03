using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionShapes;
using SkiaSharp;

namespace LibUiLogicTests.MotionShapes;

public class ShapeLinesTests
{
    private const double FrameMs = 1001.0 / 24.0;

    private static string Header(int playResX = 1920, int playResY = 1080) => $"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: {playResX}
        PlayResY: {playResY}

        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1
        Style: Box,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
        Style: Wide,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,120,100,0,0,1,0,0,7,0,0,0,1
        """;

    private static double[] Times(int count) => Enumerable.Range(0, count).Select(i => i * FrameMs).ToArray();

    private static PenPath Square(double left, double top, double size) =>
        ShapeMorph.Polygon([(left, top), (left + size, top), (left + size, top + size), (left, top + size)]);

    private static ShapeFrame Frame(int index, PenPath path, SKColor? color = null) => new() { Index = index, Path = path, Color = color };

    [Fact]
    public void Segment_SlideHoldMorph()
    {
        var frames = new List<ShapeFrame>();
        for (var f = 0; f < 30; f++)
        {
            frames.Add(Frame(f, Square(100 + 4 * f, 50, 20)));
        }

        for (var f = 30; f < 50; f++)
        {
            frames.Add(Frame(f, Square(216, 50, 20)));
        }

        for (var f = 50; f < 60; f++)
        {
            frames.Add(Frame(f, Square(216, 50, 20 + f - 49))); // growing
        }

        var segments = ShapeLines.Segment(frames, Times(60), false);

        Assert.Equal(new ShapeSegment(0, 29), segments[0]);
        Assert.Equal(new ShapeSegment(30, 49), segments[1]);
        Assert.Equal(12, segments.Count); // + one line per growing frame
    }

    [Fact]
    public void Segment_JitterBelowTolerance_OneLine()
    {
        var frames = Enumerable.Range(0, 20).Select(f => Frame(f, Square(100 + (f % 2 == 0 ? 0.2 : -0.2), 50, 20))).ToList();

        Assert.Single(ShapeLines.Segment(frames, Times(20), false));
    }

    [Fact]
    public void Segment_ColorChange_SplitsUnlessLinear()
    {
        var frames = Enumerable.Range(0, 10).Select(f => Frame(f, Square(100, 50, 20), new SKColor((byte)(100 + 10 * f), 0, 0))).ToList();
        frames[5].Color = new SKColor(250, 0, 0);

        Assert.Single(ShapeLines.Segment(frames, Times(10), false));
        Assert.True(ShapeLines.Segment(frames, Times(10), true).Count > 1);
    }

    [Fact]
    public void Write_MoveTextAndTiming()
    {
        // 20x20 square at (100,50) moving +10 px per frame over 3 frames
        var frames = Enumerable.Range(0, 3).Select(f => Frame(f, Square(100 + 10 * f, 50, 20))).ToList();
        var times = Times(3);

        var pieces = ShapeLines.Write(frames, times, ShapeLines.Segment(frames, times, false), 0, 125, Header(), "Box", 1920, 1080, string.Empty, false);

        var piece = Assert.Single(pieces);
        Assert.Equal(0, piece.StartMs);
        Assert.Equal(125, piece.EndMs);
        Assert.Equal(@"{\an7\move(0,0,20,0,0,83)\p1}m 100 50 l 120 50 l 120 70 l 100 70{\p0}", piece.Text);
    }

    [Fact]
    public void Write_HoldsAndPerFramePieces_MidpointBoundaries()
    {
        var frames = new List<ShapeFrame>
        {
            Frame(0, Square(0, 0, 10)), Frame(1, Square(0, 0, 10)),
            Frame(2, Square(0, 0, 12)), Frame(3, Square(0, 0, 14)),
        };
        var times = Times(4);

        var pieces = ShapeLines.Write(frames, times, ShapeLines.Segment(frames, times, false), 0, 170, Header(), "Box", 1920, 1080, string.Empty, false);

        Assert.Equal(3, pieces.Count);
        Assert.Equal((0.0, 60.0), (pieces[0].StartMs, pieces[0].EndMs));
        Assert.Equal((60.0, 100.0), (pieces[1].StartMs, pieces[1].EndMs));
        Assert.Equal((100.0, 170.0), (pieces[2].StartMs, pieces[2].EndMs));
        Assert.StartsWith(@"{\an7\pos(0,0)\p1}m 0 0 l 10 0", pieces[0].Text);
    }

    [Fact]
    public void Write_ScalesToPlayResAndNeutralisesStyle()
    {
        var frames = new List<ShapeFrame> { Frame(0, Square(100, 50, 20)) };

        var piece = Assert.Single(ShapeLines.Write(frames, Times(1), [new ShapeSegment(0, 0)], 0, 500, Header(384, 288), "Wide", 1920, 1080,
            @"\blur2\fad(100,0)", false));

        Assert.Equal(@"{\an7\pos(0,0)\fscx100\fscy100\frz0\blur2\fad(100,0)\p1}m 20 13.33 l 24 13.33 l 24 18.67 l 20 18.67{\p0}", piece.Text);
    }

    [Fact]
    public void Write_FadeContinuesAcrossPieces()
    {
        var frames = Enumerable.Range(0, 12).Select(f => Frame(f, Square(0, 0, 10 + f))).ToList();
        var times = Times(12);

        var pieces = ShapeLines.Write(frames, times, ShapeLines.Segment(frames, times, false), 0, 500, Header(), "Box", 1920, 1080, @"\fad(100,100)", false);

        Assert.Contains(@"\fade(255,0,255,0,100,400,500)", pieces[0].Text);
        Assert.DoesNotContain(@"\fad", pieces[5].Text); // fully opaque
        Assert.Contains(@"\fade(255,0,255,-440,-340,-40,60)", pieces[^1].Text);
    }

    [Fact]
    public void Write_TrackedColor()
    {
        var frames = Enumerable.Range(0, 3).Select(f => Frame(f, Square(0, 0, 10), new SKColor((byte)(10 * f), 0x20, 0x30))).ToList();
        var times = Times(3);

        var piece = Assert.Single(ShapeLines.Write(frames, times, ShapeLines.Segment(frames, times, true), 0, 125, Header(), "Box", 1920, 1080, string.Empty, true));

        Assert.StartsWith(@"{\an7\pos(0,0)\1c&H302000&\t(0,83,\1c&H302014&)\p1}", piece.Text);
    }

    [Fact]
    public void KeepTags_DropsPlacementAndAnimation()
    {
        var tags = ShapeLines.KeepTags(@"{\an7\pos(10,20)\blur3\fscx120\frz15\t(0,500,\blur0)\clip(0,0,5,5)\3c&H0000FF&\1c&HFF0000&\alpha&H80&\fad(200,0)\p1}m 0 0 l 1 0 1 1{\p0}", true);

        Assert.Equal(@"\blur3\3c&H0000FF&\alpha&H80&\fad(200,0)", tags);
        Assert.Equal(string.Empty, ShapeLines.KeepTags("plain text", false));
    }

    [Theory]
    [InlineData(@"{\an7\pos(10,20)\p1}m 0 0 l 10 0 10 10 0 10{\p0}", "Default", true)]
    [InlineData(@"{\p1}m 0 0 l 10 0 10 10 0 10{\p0}", "Box", true)]
    [InlineData(@"{\p1}m 0 0 l 10 0 10 10 0 10{\p0}", "Default", false)] // \an2 from the style
    [InlineData(@"{\an7\p1}m 0 0 l 10 0 10 10 0 10{\p0}", "Default", false)] // no \pos: placed at the style's margins
    [InlineData(@"{\an7\pos(10,20)\p1}m 0 0 l 10 0 10 10 m 20 20 l 30 20 30 30{\p0}", "Box", false)] // two paths
    [InlineData(@"{\an7\move(0,0,10,10)\p1}m 0 0 l 10 0 10 10{\p0}", "Box", false)]
    [InlineData(@"{\an7\frz10\p1}m 0 0 l 10 0 10 10{\p0}", "Box", false)]
    [InlineData(@"{\an7\fscx50\p1}m 0 0 l 10 0 10 10{\p0}", "Box", false)]
    [InlineData(@"{\an7\p1}m 0 0 l 10 0 10 10{\p0}", "Wide", false)]
    [InlineData("Some text", "Box", false)]
    public void Source_ReplaceOnlySimpleDrawings(string text, string style, bool replaceable)
    {
        Assert.Equal(replaceable, ShapeSource.GetReplaceablePath(text, style, Header()) != null);
    }

    [Fact]
    public void Source_NoPos_EventMarginCounts()
    {
        const string text = @"{\p1}m 0 0 l 10 0 10 10 0 10{\p0}";

        Assert.NotNull(ShapeSource.GetReplaceablePath(text, "Box", Header()));
        Assert.Null(ShapeSource.GetReplaceablePath(text, "Box", Header(), marginLeft: 40));
    }

    [Fact]
    public void Source_PathInScriptPx()
    {
        var path = ShapeSource.GetReplaceablePath(@"{\an7\pos(10,20)\p2}m 0 0 l 20 0 20 20{\p0}", "Box", Header())!;

        Assert.Equal((10.0, 20.0), (path.Anchors[0].X, path.Anchors[0].Y));
        Assert.Equal((20.0, 30.0), (path.Anchors[2].X, path.Anchors[2].Y));
    }
}
