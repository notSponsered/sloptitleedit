using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Main;

namespace UITests.Features.Main;

public class SubtitleLineViewModelTests
{
    [Fact]
    public void ToParagraph_KeepsCommentMarginsAndEffect_ForAss()
    {
        const string ass = """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 1920
            PlayResY: 1080

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,60,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,40,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Comment: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,old line kept as reference
            Dialogue: 0,0:00:03.00,0:00:04.00,Default,,20,30,40,Banner;10,Sign
            """;
        var format = new AdvancedSubStationAlpha();
        var subtitle = new Subtitle();
        format.LoadSubtitle(subtitle, ass.SplitToLines(), "test.ass");

        var lines = subtitle.Paragraphs.Select(p => new SubtitleLineViewModel(p, format)).ToList();
        subtitle.Paragraphs.Clear();
        subtitle.Paragraphs.AddRange(lines.Select(l => l.ToParagraph(format)));
        var output = format.ToText(subtitle, "test");

        Assert.Contains("Comment: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,old line kept as reference", output);
        Assert.Contains("Dialogue: 0,0:00:03.00,0:00:04.00,Default,,20,30,40,Banner;10,Sign", output);
    }

    [Fact]
    public void ToParagraph_WritesEditedMargins_AndKeepsUnchangedOnesAsWritten()
    {
        const string ass = """
            [Script Info]
            ScriptType: v4.00+

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,60,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,40,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:03.00,0:00:04.00,Default,,0020,0,0,,Sign
            """;
        var format = new AdvancedSubStationAlpha();
        var subtitle = new Subtitle();
        format.LoadSubtitle(subtitle, ass.SplitToLines(), "test.ass");

        var line = new SubtitleLineViewModel(subtitle.Paragraphs[0], format);
        Assert.Equal((20, 0, 0), (line.MarginL, line.MarginR, line.MarginV)); // shown in the Text panel

        line.MarginV = 65; // edited in the Text panel
        var copy = new SubtitleLineViewModel(line); // undo snapshots copy lines
        var p = copy.ToParagraph(format);

        Assert.Equal("0020", p.MarginL); // unchanged: the file's own spelling
        Assert.Equal("0", p.MarginR);
        Assert.Equal("65", p.MarginV);
    }
}
