using Nikse.SubtitleEdit.UiLogic.MotionShapes;
using SkiaSharp;

namespace LibUiLogicTests.MotionShapes;

public class ShapeColorTests
{
    [Fact]
    public void Sample_MedianIgnoresStripesAndOutside()
    {
        // red square with white stripes (20%) on a blue background
        using var bitmap = new SKBitmap(100, 100);
        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var inSquare = x is >= 20 and < 80 && y is >= 20 and < 80;
                bitmap.SetPixel(x, y, !inSquare ? SKColors.Blue : y % 5 == 0 ? SKColors.White : new SKColor(200, 30, 40));
            }
        }

        // the shape is 2x the frame (video px), a bit bigger than the square
        var shape = ShapeMorph.Polygon([(30, 30), (170, 30), (170, 170), (30, 170)]);

        Assert.Equal(new SKColor(200, 30, 40), ShapeColor.Sample(bitmap, shape, 2, 2));
    }

    [Fact]
    public void Smooth_RemovesSpikeAndFillsGaps()
    {
        var red = new SKColor(200, 0, 0);
        SKColor?[] colors = [null, red, red, SKColors.White, red, red, null, null];

        var smooth = ShapeColor.Smooth(colors);

        Assert.All(smooth, c => Assert.Equal(red, c));
    }

    [Theory]
    [InlineData("", 1080, false)] // missing: libass default = VSFilter-compatible
    [InlineData("YCbCr Matrix: TV.601", 1080, false)]
    [InlineData("YCbCr Matrix: TV.709", 1080, true)]
    [InlineData("YCbCr Matrix: PC.709", 480, true)]
    [InlineData("YCbCr Matrix: None", 1080, true)]
    [InlineData("YCbCr Matrix: None", 480, false)]
    [InlineData("YCbCr Matrix: None", 536, true)] // 1280x536 scope encode: decoded as 709
    public void MatrixRule(string line, int videoHeight, bool expected)
    {
        var header = "[Script Info]\nScriptType: v4.00+\n" + line + "\n\n[V4+ Styles]\n";

        Assert.Equal(expected, ShapeColor.NeedsBt709Correction(header, videoHeight == 480 ? 640 : 1280, videoHeight));
    }

    [Fact]
    public void To709_GreyUnchanged_RedCorrected()
    {
        Assert.Equal(new SKColor(128, 128, 128), ShapeColor.To709(new SKColor(128, 128, 128)));
        var red = ShapeColor.To709(new SKColor(200, 60, 60));
        Assert.True(red.Red > 200 && red.Green > 60);
    }
}
