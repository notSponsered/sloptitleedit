using Nikse.SubtitleEdit.UiLogic.Edit;

namespace LibUiLogicTests.Edit;

public class LineMoverTests
{
    private static readonly string[] Lines = ["a", "b", "c", "d", "e"];

    private static string? Move(int insertIndex, params string[] moving) =>
        LineMover.MoveTo(Lines, moving.ToHashSet(), insertIndex) is { } result ? string.Concat(result) : null;

    [Theory]
    [InlineData(4, "bcdae", "a")]   // down: dropped above "e"
    [InlineData(5, "bcdea", "a")]   // to the very end
    [InlineData(0, "eabcd", "e")]   // to the very top
    [InlineData(1, "aebcd", "e")]   // up: dropped below "a"
    [InlineData(5, "bdeac", "a", "c")] // a scattered selection lands as one block, in list order
    [InlineData(0, "bdace", "d", "b")] // selection order doesn't matter, list order does
    [InlineData(4, "abdce", "c")]   // one step down (gap 3 is right below "c" itself)
    public void MoveTo_PutsTheSelectionIntoTheGap(int insertIndex, string expected, params string[] moving)
    {
        Assert.Equal(expected, Move(insertIndex, moving));
    }

    [Theory]
    [InlineData(1, "b")] // gap right above itself
    [InlineData(2, "b")] // gap right below itself
    [InlineData(1, "b", "c")]
    [InlineData(2, "b", "c")] // inside its own block
    [InlineData(3, "b", "c")]
    [InlineData(-3, "a")] // clamped to the top, where it already is
    public void MoveTo_DropOnItself_IsNoChange(int insertIndex, params string[] moving)
    {
        Assert.Null(Move(insertIndex, moving));
    }
}
