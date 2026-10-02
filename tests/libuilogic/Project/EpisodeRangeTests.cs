using Nikse.SubtitleEdit.UiLogic.Project;

namespace LibUiLogicTests.Project;

public class EpisodeRangeTests
{
    [Theory]
    [InlineData("5", 5, null)]
    [InlineData(" 05 ", 5, null)]
    [InlineData("0", 0, null)]          // specials
    [InlineData("1-4", 1, 4)]
    [InlineData("1 – 4", 1, 4)]         // en dash, spaces
    [InlineData("E01-E04", 1, 4)]
    [InlineData("4-4", 4, null)]        // a one-episode "range"
    public void TryParseEpisodeRange_Accepts(string text, int first, int? last)
    {
        Assert.True(ProjectEpisode.TryParseEpisodeRange(text, out var f, out var l));
        Assert.Equal(first, f);
        Assert.Equal(last, l);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("4-1")]   // backwards
    [InlineData("1-2-3")]
    [InlineData("-3")]
    [InlineData("1-")]
    public void TryParseEpisodeRange_Refuses(string text)
    {
        Assert.False(ProjectEpisode.TryParseEpisodeRange(text, out _, out _));
    }
}
