using Nikse.SubtitleEdit.UiLogic.Assa;
using Nikse.SubtitleEdit.UiLogic.MotionShapes;
using Nikse.SubtitleEdit.UiLogic.MotionTracking;
using static LibUiLogicTests.SyntheticFrames;

namespace LibUiLogicTests.MotionShapes;

public class ShapeTrackerTests
{
    private const double FrameMs = 1001.0 / 24.0;

    private static double[] Times(int count) => Enumerable.Range(0, count).Select(i => i * FrameMs).ToArray();

    private static PenPath Square(double left, double top, double size) =>
        ShapeMorph.Polygon([(left, top), (left + size, top), (left + size, top + size), (left, top + size)]);

    private static PenPath Shift(PenPath path, double dx, double dy) => new Similarity(0, 0, dx, dy, 1, 0).Apply(path);

    /// <summary>4-anchor bézier ellipse, clockwise on screen.</summary>
    private static PenPath Ellipse(double cx, double cy, double rx, double ry)
    {
        const double k = 0.5523;
        var path = new PenPath();
        foreach (var (dx, dy) in new[] { (0.0, -1.0), (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0) })
        {
            var anchor = new PenAnchor(cx + dx * rx, cy + dy * ry);
            anchor.SetOutMirrored(anchor.X - dy * rx * k, anchor.Y + dx * ry * k);
            path.Anchors.Add(anchor);
        }

        return path;
    }

    private static ShapeVideo Video(IReadOnlyList<RgbFrame> frames) => new(i => frames[i], frames.Count, 1, 1);

    private static RgbFrame ToRgb(GrayFrame gray)
    {
        var rgb = new byte[gray.Pixels.Length * 3];
        for (var i = 0; i < gray.Pixels.Length; i++)
        {
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = gray.Pixels[i];
        }

        return new RgbFrame(gray.Width, gray.Height, rgb);
    }

    /// <summary>A cel-style sign: flat fill + white border on a sky gradient, anti-aliased (4x4 supersampling).</summary>
    private static RgbFrame Sign(double cx, double cy, double rx, double ry)
    {
        var pixels = new byte[FrameWidth * FrameHeight * 3];
        for (var y = 0; y < FrameHeight; y++)
        {
            for (var x = 0; x < FrameWidth; x++)
            {
                double r = 0, g = 0, b = 0;
                for (var sy = 0; sy < 4; sy++)
                {
                    for (var sx = 0; sx < 4; sx++)
                    {
                        var px = x + (sx + 0.5) / 4;
                        var py = y + (sy + 0.5) / 4;
                        var e = Math.Sqrt((px - cx) * (px - cx) / (rx * rx) + (py - cy) * (py - cy) / (ry * ry));
                        var border = 4 / Math.Min(rx, ry);
                        (double R, double G, double B) c = e > 1 ? (90, 170 + py / 4, 235) : e > 1 - border ? (255, 255, 255) : (150, 250, 240);
                        r += c.R;
                        g += c.G;
                        b += c.B;
                    }
                }

                var o = (y * FrameWidth + x) * 3;
                pixels[o] = (byte)Math.Round(r / 16);
                pixels[o + 1] = (byte)Math.Round(g / 16);
                pixels[o + 2] = (byte)Math.Round(b / 16);
            }
        }

        return new RgbFrame(FrameWidth, FrameHeight, pixels);
    }

    private static void AssertSame(PenPath expected, PenPath actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Anchors.Count, actual.Anchors.Count);
        for (var i = 0; i < expected.Anchors.Count; i++)
        {
            Assert.True(Math.Abs(expected.Anchors[i].X - actual.Anchors[i].X) <= tolerance &&
                        Math.Abs(expected.Anchors[i].Y - actual.Anchors[i].Y) <= tolerance,
                $"anchor {i}: expected ({expected.Anchors[i].X:0.###},{expected.Anchors[i].Y:0.###}) got ({actual.Anchors[i].X:0.###},{actual.Anchors[i].Y:0.###})");
        }
    }

    /// <summary>Largest distance (px, approx.) of the path's outline from the ellipse.</summary>
    private static double EllipseError(PenPath path, double cx, double cy, double rx, double ry) =>
        ShapeMorph.Flatten(path).Max(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) / (rx * rx) + (p.Y - cy) * (p.Y - cy) / (ry * ry)) - 1) * Math.Min(rx, ry));

    [Fact]
    public void Interpolates_WithoutVideo()
    {
        var a = Square(0, 0, 20);
        var b = Square(100, 40, 40);

        var frames = ShapeTracker.Compute([new ShapeKey(2, a), new ShapeKey(12, b)], Times(15), null, false, CancellationToken.None);

        Assert.Equal(15, frames.Count);
        AssertSame(a, frames[2].Path);
        AssertSame(b, frames[12].Path);
        AssertSame(Square(50, 20, 30), frames[7].Path);
        AssertSame(a, frames[0].Path); // held before the first key
        AssertSame(b, frames[14].Path);
        Assert.DoesNotContain(frames, f => f.Interpolated);
    }

    [Fact]
    public void DifferentAnchorCounts_SamePointCountInBetween()
    {
        var a = Square(0, 0, 40);
        var b = ShapeMorph.Polygon([(100, 0), (140, 20), (120, 50), (80, 50), (60, 20)]);

        var frames = ShapeTracker.Compute([new ShapeKey(0, a), new ShapeKey(6, b)], Times(7), null, false, CancellationToken.None);

        var count = frames[1].Path.Anchors.Count;
        Assert.True(count >= 32);
        Assert.All(frames.Skip(1).Take(5), f => Assert.Equal(count, f.Path.Anchors.Count));
        AssertSame(b, frames[6].Path);
    }

    [Fact]
    public void Cancelled_Throws_NotAGuessedResult()
    {
        using var cancellation = new CancellationTokenSource();
        var frame = Sign(128, 96, 40, 30);
        var video = new ShapeVideo(_ =>
        {
            cancellation.Cancel(); // Stop pressed while calculating
            return frame;
        }, 8, 1, 1);

        Assert.Throws<OperationCanceledException>(() =>
            ShapeTracker.Compute([new ShapeKey(3, Ellipse(128, 96, 40, 30))], Times(8), video, false, cancellation.Token));
    }

    [Fact]
    public void CelAnimation_ShapeComesFromThePicture_HoldsAreExact()
    {
        // a sign that squashes and stretches while drifting right, animated on 2s - a straight morph between keys can't do this
        static (double Cx, double Cy, double Rx, double Ry) Drawing(int frame)
        {
            var k = frame / 2;
            return (90 + 5 * k, 96, 50 + 8 * Math.Sin(k * 1.3), 34 - 6 * Math.Sin(k * 1.3));
        }

        var frames = Enumerable.Range(0, 12).Select(f => { var d = Drawing(f); return Sign(d.Cx, d.Cy, d.Rx, d.Ry); }).ToList();
        var first = Drawing(0);

        var result = ShapeTracker.Compute([new ShapeKey(0, Ellipse(first.Cx, first.Cy, first.Rx, first.Ry))], Times(12), Video(frames), false, CancellationToken.None);

        for (var f = 1; f < 12; f++)
        {
            var d = Drawing(f);
            var error = EllipseError(result[f].Path, d.Cx, d.Cy, d.Rx, d.Ry);
            Assert.True(error < 1.5, $"frame {f}: {error:0.00} px off");
            Assert.False(result[f].Interpolated, $"frame {f} guessed");
        }

        for (var f = 1; f < 12; f += 2)
        {
            AssertSame(result[f - 1].Path, result[f].Path); // same drawing = same shape
        }

        // so the reduction gets one line per drawing
        Assert.Equal(6, ShapeLines.Segment(result, Times(12), false).Count);
    }

    [Fact]
    public void TwoKeys_ExactAtBothEnds()
    {
        static RgbFrame At(int f) => Sign(80 + 6 * f, 96, 40 + 2 * f, 30);
        var frames = Enumerable.Range(0, 9).Select(At).ToList();
        var a = Ellipse(80, 96, 40, 30);
        var b = Ellipse(80 + 48, 96, 56, 30);

        var result = ShapeTracker.Compute([new ShapeKey(0, a), new ShapeKey(8, b)], Times(9), Video(frames), false, CancellationToken.None);

        AssertSame(a, result[0].Path);
        AssertSame(b, result[8].Path);
        Assert.True(EllipseError(result[4].Path, 80 + 24, 96, 48, 30) < 1.5);
    }

    [Fact]
    public void SlowPan_IsNotFrozenAsAHold()
    {
        // 0.3 px per frame: each frame looks "held" next to the one before, but it adds up
        var frames = Enumerable.Range(0, 14).Select(i => Sign(100 + 0.3 * i, 96, 40, 30)).ToList();

        var result = ShapeTracker.Compute([new ShapeKey(0, Ellipse(100, 96, 40, 30))], Times(14), Video(frames), false, CancellationToken.None);

        Assert.True(EllipseError(result[13].Path, 100 + 0.3 * 13, 96, 40, 30) < 1.2);
    }

    [Fact]
    public void KeyInsideAHold_ShapesTheWholeDrawing()
    {
        // frames 2-5 are one drawing; the key is on frame 4
        static RgbFrame At(int f) => f is >= 2 and <= 5 ? Sign(130, 96, 45, 30) : Sign(100 + 10 * f, 96, 40, 30);
        var frames = Enumerable.Range(0, 8).Select(At).ToList();
        var key = Ellipse(131, 96, 46, 30); // drawn a little off, as users do

        var result = ShapeTracker.Compute([new ShapeKey(4, key)], Times(8), Video(frames), false, CancellationToken.None);

        for (var f = 2; f <= 5; f++)
        {
            AssertSame(key, result[f].Path);
        }
    }

    [Fact]
    public void TwoKeys_AnchorOrderStaysTheSame()
    {
        // the second key is the same circle drawn starting at another point: every frame in between must keep one anchor order
        static RgbFrame At(int f) => Sign(80 + 6 * f, 96, 35, 35);
        var frames = Enumerable.Range(0, 9).Select(At).ToList();
        var a = Ellipse(80, 96, 35, 35);
        var b = ShapeMorph.Reorder(Ellipse(128, 96, 35, 35), (false, 2));

        var result = ShapeTracker.Compute([new ShapeKey(0, a), new ShapeKey(8, b)], Times(9), Video(frames), false, CancellationToken.None);

        for (var f = 1; f < 8; f++)
        {
            var top = result[f].Path.Anchors[0];
            Assert.True(top.Y < 96 - 25, $"frame {f}: anchor 0 at ({top.X:0},{top.Y:0}) is not the top one");
        }
    }

    [Fact]
    public void Texture_OneKey_FollowsIt()
    {
        const double vx = 1.5;
        const double vy = 0.7;
        var texture = MakeTexture(7);
        var frames = Enumerable.Range(0, 8).Select(i => ToRgb(MakeFrame(texture, (x, y) => (x - vx * i, y - vy * i), 1, 0))).ToList();
        var key = Square(100, 70, 40);

        var result = ShapeTracker.Compute([new ShapeKey(3, key)], Times(8), Video(frames), false, CancellationToken.None);

        for (var f = 0; f < 8; f++)
        {
            AssertSame(Shift(key, vx * (f - 3), vy * (f - 3)), result[f].Path, 0.75);
        }
    }

    [Fact]
    public void FlatPanel_TracksItsEdges()
    {
        var frames = Enumerable.Range(0, 6).Select(i => ToRgb(FlatRectangle(60 + 2.5 * i, 50, 50, 40))).ToList();
        var key = ShapeMorph.Polygon([(60, 50), (110, 50), (110, 90), (60, 90)]);

        var result = ShapeTracker.Compute([new ShapeKey(0, key)], Times(6), Video(frames), false, CancellationToken.None);

        AssertSame(Shift(key, 12.5, 0), result[5].Path, 0.5);
        Assert.False(result[5].Interpolated);
    }
}
