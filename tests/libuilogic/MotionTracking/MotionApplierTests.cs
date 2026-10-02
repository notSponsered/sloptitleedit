using Nikse.SubtitleEdit.UiLogic.MotionTracking;

namespace LibUiLogicTests.MotionTracking;

public class MotionApplierTests
{
    private const string Header = """
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 1920
        PlayResY: 1080

        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
        Style: Default,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,2,2,10,10,10,1
        Style: Box,Arial,40,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
        """;

    private const double FrameMs = 1001.0 / 24.0; // 23.976 fps

    /// <summary>Frames 20-32, moving +10 px/frame in x; frame 24 (1001 ms) is the reference.</summary>
    private static List<MotionSample> Samples(double scale = 1, double rotation = 0) =>
        Enumerable.Range(20, 13)
            .Select(n => new MotionSample(n * FrameMs / 1000.0, 500 + 10 * (n - 24), 300, n == 24 ? 1 : scale, n == 24 ? 0 : rotation, 1))
            .ToList();

    [Fact]
    public void Apply_SplitsPerFrame_WithShiftedFadeAndTransform()
    {
        var pieces = MotionApplier.Apply("{\\fad(100,50)\\t(\\fscx120)}Sign", "Default", 1000, 1200, Header, 1920, 1080, Samples(), 1.001)!;

        Assert.Equal(5, pieces.Count);
        Assert.Equal(1000, pieces[0].StartMs);
        Assert.Equal(1200, pieces[^1].EndMs);
        Assert.Equal(new double[] { 1020, 1060, 1110, 1150 }, pieces.Skip(1).Select(p => p.StartMs));
        for (var i = 1; i < pieces.Count; i++)
        {
            Assert.Equal(pieces[i - 1].EndMs, pieces[i].StartMs);
            var previousFrame = (24 + i - 1) * FrameMs;
            var frame = (24 + i) * FrameMs;
            Assert.True(pieces[i].StartMs > previousFrame && pieces[i].StartMs < frame, $"boundary {pieces[i].StartMs}");
        }

        // an2 anchor = (PlayResX/2, PlayResY - MarginV), +10 px per frame
        Assert.Equal("{\\pos(960,1070)\\fade(255,0,255,0,100,150,200)\\t(0,200,\\fscx120)}Sign", pieces[0].Text);
        Assert.Equal("{\\pos(970,1070)\\fade(255,0,255,-20,80,130,180)\\t(-20,180,\\fscx120)}Sign", pieces[1].Text);
        Assert.Equal("{\\pos(990,1070)\\t(-110,90,\\fscx120)}Sign", pieces[3].Text); // opaque middle: no fade needed
        Assert.Equal("{\\pos(1000,1070)\\fade(255,0,255,-150,-50,0,50)\\t(-150,50,\\fscx120)}Sign", pieces[4].Text);
    }

    [Fact]
    public void Apply_BackgroundDrawing_GetsPosFromZeroAnchor()
    {
        var pieces = MotionApplier.Apply("{\\p1}m 0 0 l 10 0 10 10{\\p0}", "Box", 1000, 1100, Header, 1920, 1080, Samples(), 1.001)!;

        Assert.Equal("{\\pos(0,0)\\p1}m 0 0 l 10 0 10 10{\\p0}", pieces[0].Text);
        Assert.Equal("{\\pos(10,0)\\p1}m 0 0 l 10 0 10 10{\\p0}", pieces[1].Text);
    }

    [Fact]
    public void Apply_ExistingPosAndClip_AreTranslated_ScaledToPlayRes()
    {
        var header = Header.Replace("PlayResX: 1920", "PlayResX: 960").Replace("PlayResY: 1080", "PlayResY: 540");
        var pieces = MotionApplier.Apply("{\\pos(100,50)\\clip(0,0,20,20)}x", "Default", 1000, 1100, header, 1920, 1080, Samples(), 1.001)!;

        Assert.Equal("{\\pos(105,50)\\clip(5,0,25,20)}x", pieces[1].Text); // 10 video px = 5 script px
    }

    [Fact]
    public void Apply_ScaleAndRotation()
    {
        // frame 25: P = (510,300), scale 2, rotated 90° clockwise around the tracked point
        var pieces = MotionApplier.Apply("{\\pos(960,1070)}x", "Default", 1000, 1100, Header, 1920, 1080, Samples(2, 90), 1.001)!;

        // A - Pref = (460,770) -> rotate 90° cw (y down) -> (-770,460) -> * 2 -> (-1540,920) + (510,300)
        Assert.Equal("{\\pos(-1030,1220)\\frz-90\\fscx200\\fscy200}x", pieces[1].Text);
    }

    [Fact]
    public void Apply_LineOutsideTrack_ReturnsNull()
    {
        Assert.Null(MotionApplier.Apply("x", "Default", 5000, 6000, Header, 1920, 1080, Samples(), 1.001));
    }
}
