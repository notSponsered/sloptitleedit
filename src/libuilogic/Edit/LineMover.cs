namespace Nikse.SubtitleEdit.UiLogic.Edit;

/// <summary>Reordering for drag-and-drop in the subtitle list.</summary>
public static class LineMover
{
    /// <summary>
    /// The list with <paramref name="moving"/> (kept in their current order) moved into the gap before
    /// <paramref name="insertIndex"/> — an index into the list as it is now, 0..Count. Null when that
    /// changes nothing (e.g. a block dropped right above or below itself).
    /// </summary>
    public static List<T>? MoveTo<T>(IReadOnlyList<T> list, IReadOnlySet<T> moving, int insertIndex)
    {
        insertIndex = Math.Clamp(insertIndex, 0, list.Count);
        var staying = new List<T>(list.Count);
        var moved = new List<T>();
        var stayingAbove = 0;
        for (var i = 0; i < list.Count; i++)
        {
            if (moving.Contains(list[i]))
            {
                moved.Add(list[i]);
            }
            else
            {
                staying.Add(list[i]);
                if (i < insertIndex)
                {
                    stayingAbove++;
                }
            }
        }

        if (moved.Count == 0)
        {
            return null;
        }

        staying.InsertRange(stayingAbove, moved);
        return staying.SequenceEqual(list) ? null : staying;
    }
}
