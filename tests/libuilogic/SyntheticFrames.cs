using Nikse.SubtitleEdit.UiLogic.MotionTracking;

namespace LibUiLogicTests;

/// <summary>Synthetic test frames: smoothed seeded noise (trackable texture) sampled through any mapping.</summary>
internal static class SyntheticFrames
{
    internal const int FrameWidth = 256;
    internal const int FrameHeight = 192;

    /// <summary>Smoothed seeded noise, larger than a frame, with frame (0,0) at texture (72,54).</summary>
    internal static double[,] MakeTexture(int seed)
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

    /// <summary>A frame where pixel (x,y) shows the texture at <paramref name="sourceOf"/>(x,y).</summary>
    internal static GrayFrame MakeFrame(double[,] texture, Func<double, double, (double X, double Y)> sourceOf, double gain, double offset)
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

    /// <summary>A flat grey frame with one flat-colour rectangle (texture only at its edges).</summary>
    internal static GrayFrame FlatRectangle(double left, double top, double width, double height, byte inside = 200, byte outside = 60)
    {
        var pixels = new byte[FrameWidth * FrameHeight];
        for (var y = 0; y < FrameHeight; y++)
        {
            for (var x = 0; x < FrameWidth; x++)
            {
                // anti-aliased coverage so sub-pixel positions are visible to the tracker
                var cx = Math.Clamp(Math.Min(x + 1 - left, left + width - x), 0, 1);
                var cy = Math.Clamp(Math.Min(y + 1 - top, top + height - y), 0, 1);
                var coverage = cx * cy;
                pixels[y * FrameWidth + x] = (byte)Math.Round(outside + (inside - outside) * coverage);
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
