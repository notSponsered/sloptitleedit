using Nikse.SubtitleEdit.UiLogic.Edit;

namespace LibUiLogicTests.Edit;

public class TimelineLanesTests
{
    /// <summary>"0-10 5-15" → lanes as "lane/lanes" per span, e.g. "0/2 1/2".</summary>
    private static string Lanes(string spans)
    {
        var list = spans.Split(' ').Select(s => s.Split('-')).Select(a => (double.Parse(a[0]), double.Parse(a[1]))).ToList();
        return string.Join(" ", TimelineLanes.Assign(list).Select(r => $"{r.Lane}/{r.Lanes}"));
    }

    [Theory]
    [InlineData("0-10 20-30", "0/1 0/1")]                  // no overlap: full height
    [InlineData("0-10 10-20", "0/1 0/1")]                  // touching (razor cut) is not overlapping
    [InlineData("0-10 5-15", "0/2 1/2")]                   // two overlapping
    [InlineData("0-10 5-15 20-30", "0/2 1/2 0/1")]         // the group ends; the next line is full height again
    [InlineData("0-10 5-15 12-20", "0/2 1/2 0/2")]         // a chain reuses the freed top lane
    [InlineData("0-30 5-10 15-20", "0/2 1/2 1/2")]         // a long line with two short ones under it
    [InlineData("0-10 1-10 2-10", "0/3 1/3 2/3")]          // three on top of each other
    public void Assign_StacksOverlapsIntoLanes(string spans, string expected)
    {
        Assert.Equal(expected, Lanes(spans));
    }

    [Theory]
    [InlineData("0-10 5-15", "1 2", "0/2 1/2")]               // higher priority (lower number) on top
    [InlineData("0-10 5-15", "2 1", "1/2 0/2")]               // the later line can be on top
    [InlineData("0-30 5-10 15-20", "2 1 1", "1/2 0/2 0/2")]   // two short lines share the top lane above a long one
    [InlineData("0-10 1-10 2-10", "3 1 2", "2/3 0/3 1/3")]
    [InlineData("0-10 5-15 20-30 25-35", "2 1 2 1", "1/2 0/2 1/2 0/2")] // each group ordered on its own
    public void Assign_PriorityDecidesTopToBottom(string spans, string priorities, string expected)
    {
        var list = spans.Split(' ').Select(s => s.Split('-')).Select(a => (double.Parse(a[0]), double.Parse(a[1]))).ToList();
        var rank = priorities.Split(' ').Select(int.Parse).ToArray();
        var result = TimelineLanes.Assign(list, (a, b) => rank[a].CompareTo(rank[b]));
        Assert.Equal(expected, string.Join(" ", result.Select(r => $"{r.Lane}/{r.Lanes}")));
    }

    [Fact]
    public void Assign_Empty()
    {
        Assert.Empty(TimelineLanes.Assign([]));
    }
}
