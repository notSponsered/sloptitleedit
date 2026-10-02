using Nikse.SubtitleEdit.UiLogic.Edit;

namespace LibUiLogicTests.Edit;

public class TextPaddingTests
{
    /// <summary>The padding runs as the text they cover, joined by "|", e.g. "  |\h".</summary>
    private static string Padding(string line) =>
        string.Join("|", TextPadding.Find(line).Select(r => line.Substring(r.Start, r.Length)));

    [Theory]
    [InlineData("Hello", "")]
    [InlineData("Hello world", "")]                      // spaces between words are not padding
    [InlineData("  Hello", "  ")]
    [InlineData("Hello  ", "  ")]
    [InlineData(" Hello ", " | ")]
    [InlineData(@"\hHello\h\h", @"\h|\h\h")]             // ASS hard spaces
    [InlineData(@" \h Hello", @" \h ")]                  // touching runs join
    [InlineData(@"{\an8}  Exit", "  ")]                  // after a leading tag block
    [InlineData(@"{\an8} {\c&H00FF00&} Exit", " | ")]    // between leading tag blocks
    [InlineData(@"Exit  {\fad(200,200)}", "  ")]         // before a trailing tag block
    [InlineData(@"{\i1}Hi{\i0}", "")]
    [InlineData("   ", "   ")]                           // nothing visible: all padding, once
    [InlineData("", "")]
    public void Find_MarksWhitespaceAtTheEdges(string line, string expected)
    {
        Assert.Equal(expected, Padding(line));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData(@"\h", true)]
    [InlineData(@"{\an8}", true)]
    [InlineData(@"{\an8} \h", true)]
    [InlineData("a", false)]
    [InlineData(@"{\an8}a", false)]
    public void IsBlank(string line, bool expected)
    {
        Assert.Equal(expected, TextPadding.IsBlank(line));
    }
}
