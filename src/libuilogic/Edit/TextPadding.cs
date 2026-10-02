using System.Collections.Generic;

namespace Nikse.SubtitleEdit.UiLogic.Edit;

/// <summary>
/// Finds the padding in one line of subtitle text, so the text box can show it: whitespace (spaces, tabs,
/// no-break spaces and the ASS hard space \h) before the first and after the last visible character.
/// {Override} blocks at either end are skipped, so in "{\an8}  Hi" the two spaces count as padding.
/// </summary>
public static class TextPadding
{
    /// <summary>(start, length) of each padding run, left to right. A line with nothing visible is all padding.</summary>
    public static List<(int Start, int Length)> Find(string line)
    {
        var result = new List<(int Start, int Length)>();

        // left: from the start up to the first visible character
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == '{' && line.IndexOf('}', i) is var close and > 0)
            {
                i = close + 1;
                continue;
            }

            var length = SpaceLength(line, i);
            if (length == 0)
            {
                break; // first visible character
            }

            Add(i, length);
            i += length;
        }

        if (i >= line.Length)
        {
            return result; // nothing visible: the left pass already covered every space
        }

        // right: from the end back to the last visible character
        var right = new List<(int Start, int Length)>();
        var end = line.Length;
        while (end > i)
        {
            if (line[end - 1] == '}' && line.LastIndexOf('{', end - 1) is var open and >= 0 && open >= i)
            {
                end = open;
                continue;
            }

            var length = end >= 2 && SpaceLength(line, end - 2) == 2 ? 2 : SpaceLength(line, end - 1);
            if (length == 0)
            {
                break; // last visible character
            }

            end -= length;
            right.Add((end, length));
        }

        right.Reverse();
        foreach (var (start, length) in right)
        {
            Add(start, length);
        }

        return result;

        void Add(int start, int length) // joins touching runs, e.g. " \h " is one run
        {
            if (result.Count > 0 && result[^1].Start + result[^1].Length == start)
            {
                result[^1] = (result[^1].Start, result[^1].Length + length);
            }
            else
            {
                result.Add((start, length));
            }
        }
    }

    /// <summary>True when the line shows nothing (empty, only whitespace and/or override blocks).</summary>
    public static bool IsBlank(string line) => StripTags(line).Replace("\\h", string.Empty).Trim().Length == 0;

    /// <summary>Length of the whitespace at <paramref name="i"/>: 2 for \h, 1 for a space/tab/no-break space, else 0.</summary>
    private static int SpaceLength(string line, int i)
    {
        if (i < 0 || i >= line.Length)
        {
            return 0;
        }

        if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == 'h')
        {
            return 2;
        }

        return line[i] is ' ' or '\t' or ' ' ? 1 : 0;
    }

    private static string StripTags(string line)
    {
        var sb = new System.Text.StringBuilder(line.Length);
        var depth = 0;
        foreach (var c in line)
        {
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && depth > 0)
            {
                depth--;
            }
            else if (depth == 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
