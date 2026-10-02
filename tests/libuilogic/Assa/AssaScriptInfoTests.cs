using Nikse.SubtitleEdit.UiLogic.Assa;

namespace LibUiLogicTests.Assa;

public class AssaScriptInfoTests
{
    private const string Header =
        "[Script Info]\r\n; comment: not a key\r\nTitle: Episode 1\r\nScriptType: v4.00+\r\nYCbCr Matrix: TV.709\r\nPlayResX: 1920\r\n\r\n" +
        "[V4+ Styles]\r\nFormat: Name, Fontname\r\nStyle: Default,Arial\r\n";

    [Fact]
    public void Get_ReadsKeysWithSpaces_CaseInsensitive()
    {
        Assert.Equal("TV.709", AssaScriptInfo.Get(Header, "YCbCr Matrix"));
        Assert.Equal("1920", AssaScriptInfo.Get(Header, "playresx"));
        Assert.Null(AssaScriptInfo.Get(Header, "WrapStyle"));
        Assert.Null(AssaScriptInfo.Get(Header, "Format")); // other sections are ignored
    }

    [Fact]
    public void Set_ReplacesExistingLine_KeepsEverythingElse()
    {
        var result = AssaScriptInfo.Set(Header, "YCbCr Matrix", "TV.601");

        Assert.Equal(Header.Replace("YCbCr Matrix: TV.709", "YCbCr Matrix: TV.601"), result);
    }

    [Fact]
    public void Set_AddsMissingKey_AtEndOfSection()
    {
        var result = AssaScriptInfo.Set(Header, "WrapStyle", "2");

        Assert.Contains("PlayResX: 1920\r\nWrapStyle: 2\r\n\r\n[V4+ Styles]", result);
        Assert.Equal("2", AssaScriptInfo.Get(result, "WrapStyle"));
    }

    [Fact]
    public void Set_CreatesSection_WhenMissing()
    {
        var result = AssaScriptInfo.Set(string.Empty, "Title", "x");

        Assert.Equal("x", AssaScriptInfo.Get(result, "Title"));
    }
}
