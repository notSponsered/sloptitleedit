using Nikse.SubtitleEdit.UiLogic.MotionTracking;

namespace LibUiLogicTests.MotionTracking;

public class MotionTrackerTests
{
    private const int FrameWidth = 256;
    private const int FrameHeight = 192;

    [Fact]
    public void Track_Translation_WithBrightnessChange_IsSubPixelAccurate()
    {
        var texture = MakeTexture(42);
        var frames = Enumerable.Range(0, 15).Select(k =>
            MakeFrame(texture, (x, y) => (x - 1.7 * k, y + 0.9 * k), gain: 1 - 0.02 * k, offset: 5)).ToList();

        var box = new TrackBox(112, 80, 32, 32);
        var points = MotionTracker.Track(k => frames[k], 0, 14, box, false, CancellationToken.None);

        Assert.Equal(15, points.Count);
        foreach (var p in points)
        {
            var k = p.Index;
            Assert.True(Math.Abs(p.X - (128 + 1.7 * k)) < 0.3, $"frame {k}: x {p.X}");
            Assert.True(Math.Abs(p.Y - (96 - 0.9 * k)) < 0.3, $"frame {k}: y {p.Y}");
            Assert.True(p.Score > 0.9, $"frame {k}: score {p.Score}");
        }
    }

    [Fact]
    public void Track_Backward_FindsSameMotion()
    {
        var texture = MakeTexture(7);
        var frames = Enumerable.Range(0, 8).Select(k => MakeFrame(texture, (x, y) => (x - 2.0 * k, y), 1, 0)).ToList();

        var points = MotionTracker.Track(k => frames[k], 7, 0, new TrackBox(126, 80, 32, 32), false, CancellationToken.None);

        Assert.Equal(8, points.Count);
        Assert.Equal(0, points[^1].Index);
        Assert.True(Math.Abs(points[^1].X - (142 - 14)) < 0.3, $"x {points[^1].X}");
    }

    [Fact]
    public void Track_ScaleAndRotation_IsRecovered()
    {
        var texture = MakeTexture(3);
        const double cx = 128, cy = 96;
        var frames = Enumerable.Range(0, 10).Select(k =>
        {
            var s = 1 + 0.01 * k;
            var theta = 0.5 * k * Math.PI / 180;
            var (tx, ty) = (1.0 * k, 0.5 * k);
            // inverse of: p' = c + s R (p - c) + t
            return MakeFrame(texture, (x, y) =>
            {
                var dx = x - cx - tx;
                var dy = y - cy - ty;
                return (cx + (Math.Cos(theta) * dx + Math.Sin(theta) * dy) / s,
                        cy + (-Math.Sin(theta) * dx + Math.Cos(theta) * dy) / s);
            }, 1, 0);
        }).ToList();

        var points = MotionTracker.Track(k => frames[k], 0, 9, new TrackBox(80, 48, 96, 96), true, CancellationToken.None);

        Assert.Equal(10, points.Count);
        var last = points[^1];
        Assert.True(Math.Abs(last.Scale - 1.09) < 0.01, $"scale {last.Scale}");
        Assert.True(Math.Abs(last.Rotation - 4.5) < 0.5, $"rotation {last.Rotation}");
        Assert.True(Math.Abs(last.X - (cx + 9)) < 0.5 && Math.Abs(last.Y - (cy + 4.5)) < 0.5, $"center {last.X},{last.Y}");
    }

    [Fact]
    public void Track_AnimationOnTwos_WithBigJumps()
    {
        // holds for 2 frames, then jumps 30 px (bigger than the 24 px search radius of a 32 px box), and one 80 px jump
        var texture = MakeTexture(11);
        var offsets = new[] { 0, 0, 30, 30, 60, 60, 140, 140, 110, 110 };
        var frames = offsets.Select(o => MakeFrame(texture, (x, y) => (x - o * 0.5, y - o * 0.25), 1, 0)).ToList();

        var points = MotionTracker.Track(k => frames[k], 0, frames.Count - 1, new TrackBox(70, 70, 32, 32), false, CancellationToken.None);

        Assert.Equal(frames.Count, points.Count);
        foreach (var p in points)
        {
            Assert.True(Math.Abs(p.X - (86 + offsets[p.Index] * 0.5)) < 0.5 && Math.Abs(p.Y - (86 + offsets[p.Index] * 0.25)) < 0.5,
                $"frame {p.Index}: {p.X},{p.Y}");
        }
    }

    [Fact]
    public void Track_MultiplePoints_KeepsGoingWhenOnePointIsCovered()
    {
        var texture = MakeTexture(5);
        var frames = Enumerable.Range(0, 12).Select(k =>
        {
            var frame = MakeFrame(texture, (x, y) => (x - 2.0 * k, y - 1.0 * k), 1, 0);
            if (k >= 5)
            {
                // something covers the first point
                var covered = new Random(k);
                for (var y = 30; y < 90; y++)
                {
                    for (var x = 30; x < 110; x++)
                    {
                        frame.Pixels[y * frame.Width + x] = (byte)covered.Next(256);
                    }
                }
            }

            return frame;
        }).ToList();

        var boxes = new[] { new TrackBox(40, 40, 32, 32), new TrackBox(150, 110, 32, 32) };
        var points = MotionTracker.Track(k => frames[k], 0, 11, boxes, false, CancellationToken.None);

        Assert.Equal(12, points.Count);
        var last = points[^1];
        // anchor = center of both boxes (111, 91) moved by (22, 11)
        Assert.True(Math.Abs(last.X - 133) < 0.5 && Math.Abs(last.Y - 102) < 0.5, $"anchor {last.X},{last.Y}");
        Assert.Equal(2, last.Centers!.Length);
    }

    [Fact]
    public void Track_TwoPoints_GiveScaleAndRotation()
    {
        var texture = MakeTexture(9);
        const double cx = 128, cy = 96;
        var frames = Enumerable.Range(0, 8).Select(k =>
        {
            var s = 1 + 0.01 * k;
            var theta = 0.5 * k * Math.PI / 180;
            return MakeFrame(texture, (x, y) =>
            {
                var dx = x - cx;
                var dy = y - cy;
                return (cx + (Math.Cos(theta) * dx + Math.Sin(theta) * dy) / s, cy + (-Math.Sin(theta) * dx + Math.Cos(theta) * dy) / s);
            }, 1, 0);
        }).ToList();

        var boxes = new[] { new TrackBox(50, 60, 40, 40), new TrackBox(170, 90, 40, 40) };
        var points = MotionTracker.Track(k => frames[k], 0, 7, boxes, true, CancellationToken.None);

        Assert.Equal(8, points.Count);
        Assert.True(Math.Abs(points[^1].Scale - 1.07) < 0.01, $"scale {points[^1].Scale}");
        Assert.True(Math.Abs(points[^1].Rotation - 3.5) < 0.5, $"rotation {points[^1].Rotation}");
    }

    [Fact]
    public void HasEnoughDetail_RejectsFlatBox()
    {
        var flat = new GrayFrame(64, 64, Enumerable.Repeat((byte)100, 64 * 64).ToArray());
        Assert.False(MotionTracker.HasEnoughDetail(flat, new TrackBox(10, 10, 20, 20)));
    }

    /// <summary>Smoothed seeded noise, larger than a frame, with frame (0,0) at texture (72,54).</summary>
    private static double[,] MakeTexture(int seed)
    {
        var random = new Random(seed);
        var texture = new double[300, 400];
        for (var y = 0; y < 300; y++)
        {
            for (var x = 0; x < 400; x++)
            {
                texture[y, x] = random.NextDouble() * 255;
            }
        }

        for (var pass = 0; pass < 2; pass++)
        {
            var blurred = new double[300, 400];
            for (var y = 1; y < 299; y++)
            {
                for (var x = 1; x < 399; x++)
                {
                    double sum = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            sum += texture[y + dy, x + dx];
                        }
                    }

                    blurred[y, x] = sum / 9;
                }
            }

            texture = blurred;
        }

        return texture;
    }

    private static GrayFrame MakeFrame(double[,] texture, Func<double, double, (double X, double Y)> sourceOf, double gain, double offset)
    {
        var pixels = new byte[FrameWidth * FrameHeight];
        for (var y = 0; y < FrameHeight; y++)
        {
            for (var x = 0; x < FrameWidth; x++)
            {
                var (sx, sy) = sourceOf(x, y);
                var v = 128 + (Bilinear(texture, sx + 72, sy + 54) - 127.5) * 3;
                pixels[y * FrameWidth + x] = (byte)Math.Clamp(v * gain + offset, 0, 255);
            }
        }

        return new GrayFrame(FrameWidth, FrameHeight, pixels);
    }

    private static double Bilinear(double[,] t, double x, double y)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        return t[y0, x0] * (1 - fx) * (1 - fy) + t[y0, x0 + 1] * fx * (1 - fy) +
               t[y0 + 1, x0] * (1 - fx) * fy + t[y0 + 1, x0 + 1] * fx * fy;
    }
}
