using System.Text.RegularExpressions;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Edit.MultipleReplace;

namespace UITests.Features.Edit;

public class MultipleReplaceEngineTests
{
    private static string Run(string text, params (string Find, string Replace, MultipleReplaceType Type)[] rules)
    {
        var cache = new Dictionary<string, Regex>();
        var expressions = rules
            .Select(r => MultipleReplaceEngine.Make(r.Find, r.Replace, r.Type, string.Empty, cache))
            .OfType<ReplaceExpression>()
            .ToList();
        return MultipleReplaceEngine.Apply(text, expressions, cache);
    }

    [Fact]
    public void CaseSensitive_OnlyMatchesExactCase()
    {
        Assert.Equal("Bye bye", Run("Hi bye", ("Hi", "Bye", MultipleReplaceType.CaseSensitive)));
        Assert.Equal("hi bye", Run("hi bye", ("Hi", "Bye", MultipleReplaceType.CaseSensitive)));
    }

    [Fact]
    public void CaseInsensitive_ReplacesEveryHit_IncludingWhenReplacementContainsFind()
    {
        Assert.Equal("aa x aa", Run("A x a", ("a", "aa", MultipleReplaceType.CaseInsensitive)));
    }

    [Fact]
    public void Regex_WorksAcrossNewLine()
    {
        var text = "one" + Environment.NewLine + "two";
        Assert.Equal("one two", Run(text, (@"\n", " ", MultipleReplaceType.RegularExpression)));
    }

    [Fact]
    public void Rules_AreAppliedInOrder()
    {
        Assert.Equal("c", Run("a", ("a", "b", MultipleReplaceType.CaseSensitive), ("b", "c", MultipleReplaceType.CaseSensitive)));
    }

    [Fact]
    public void NoHit_LeavesTextAndHitsUnchanged()
    {
        var cache = new Dictionary<string, Regex>();
        var hits = new List<ReplaceExpression>();
        var e = MultipleReplaceEngine.Make("zzz", "y", MultipleReplaceType.CaseInsensitive, string.Empty, cache)!;

        Assert.Equal("abc", MultipleReplaceEngine.Apply("abc", [e], cache, hits));
        Assert.Empty(hits);
    }

    [Fact]
    public void EmptyFindAndInvalidRegex_AreSkipped()
    {
        var cache = new Dictionary<string, Regex>();

        Assert.Null(MultipleReplaceEngine.Make(string.Empty, "x", MultipleReplaceType.CaseInsensitive, string.Empty, cache));
        Assert.Null(MultipleReplaceEngine.Make("(", "x", MultipleReplaceType.RegularExpression, string.Empty, cache));
    }
}
