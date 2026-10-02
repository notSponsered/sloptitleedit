namespace Nikse.SubtitleEdit.UiLogic.Edit;

/// <summary>
/// Stacks overlapping subtitles into lanes for the waveform, like tracks in a video editor. Lines that overlap
/// nothing keep the full height (one lane); a group of lines that overlap each other splits its height into
/// as many lanes as it needs.
/// </summary>
public static class TimelineLanes
{
    /// <summary>
    /// For each span (sorted by start), its lane (0 = top) and the number of lanes in its overlap group.
    /// Spans that only touch (end == next start, e.g. after a razor cut) do not overlap.
    /// <paramref name="topFirst"/> compares two span indexes: the one that sorts first gets the higher lane
    /// (ties and no comparison: earlier start first). It decides the order, not the time, so a line keeps its
    /// place above or below its neighbours while it is dragged sideways.
    /// </summary>
    public static (int Lane, int Lanes)[] Assign(IReadOnlyList<(double Start, double End)> spans, Comparison<int>? topFirst = null)
    {
        var result = new (int Lane, int Lanes)[spans.Count];
        var groupStart = 0;
        var groupEnd = double.MinValue;
        for (var i = 0; i < spans.Count; i++)
        {
            if (spans[i].Start >= groupEnd)
            {
                PlaceGroup(groupStart, i); // nothing still running: a new group starts here
                groupStart = i;
            }

            groupEnd = Math.Max(groupEnd, spans[i].End);
        }

        PlaceGroup(groupStart, spans.Count);
        return result;

        // Top lane first: each span (in priority order) takes the highest lane where it overlaps nothing.
        // ponytail: O(n²) per overlap group, fine for the few lines that overlap at one time.
        void PlaceGroup(int from, int to)
        {
            var order = Enumerable.Range(from, to - from);
            if (topFirst != null)
            {
                order = order.OrderBy(i => i, Comparer<int>.Create(topFirst)); // stable: ties keep start order
            }

            var lanes = new List<List<int>>();
            foreach (var i in order)
            {
                var lane = lanes.FindIndex(l => l.All(j => spans[j].End <= spans[i].Start || spans[i].End <= spans[j].Start));
                if (lane < 0)
                {
                    lane = lanes.Count;
                    lanes.Add([]);
                }

                lanes[lane].Add(i);
                result[i].Lane = lane;
            }

            for (var i = from; i < to; i++)
            {
                result[i].Lanes = lanes.Count;
            }
        }
    }
}
