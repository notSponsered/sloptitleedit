using Nikse.SubtitleEdit.UiLogic.Assa;

namespace LibUiLogicTests.Assa;

public class AssaTagsTests
{
    [Fact]
    public void SetFade_KeepsOtherTags_ReplacesExistingFad()
    {
        Assert.Equal("{\\fad(500,200)\\b1}Hi", AssaTags.SetFade("{\\fad(100,200)\\b1}Hi", 500, 200));
    }

    [Fact]
    public void SetFade_NoTags_PrependsBlock()
    {
        Assert.Equal("{\\fad(500,0)}Hi", AssaTags.SetFade("Hi", 500, 0));
    }

    [Fact]
    public void SetFade_ReplacesComplexFade_AndDropsEmptyBlock()
    {
        Assert.Equal("{\\fad(10,20)}x", AssaTags.SetFade("{\\fade(255,0,255,0,1,2,3)}x", 10, 20));
    }

    [Fact]
    public void GetFade_ReadsValues()
    {
        Assert.Equal((100, 200), AssaTags.GetFade("{\\b1\\fad(100, 200)}Hi"));
        Assert.Null(AssaTags.GetFade("{\\fade(255,0,255,0,1,2,3)}x"));
    }

    [Fact]
    public void InsertIntoFirstBlock_CommentBlockIsNotAnOverrideBlock()
    {
        Assert.Equal("{\\pos(1,2)}{note}Hi", AssaTags.InsertIntoFirstBlock("{note}Hi", "\\pos(1,2)"));
        Assert.Equal("{\\pos(1,2)\\an7\\p1}m 0 0", AssaTags.InsertIntoFirstBlock("{\\an7\\p1}m 0 0", "\\pos(1,2)"));
    }

    [Fact]
    public void FirstAlignment_FindsInlineAn()
    {
        Assert.Equal(8, AssaTags.FirstAlignment("{\\b1\\an8}x"));
        Assert.Null(AssaTags.FirstAlignment("x"));
    }
}
