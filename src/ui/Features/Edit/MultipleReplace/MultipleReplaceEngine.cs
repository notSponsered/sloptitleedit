using Nikse.SubtitleEdit.Core.Common;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.Features.Edit.MultipleReplace;

/// <summary>Rule application shared by the Multiple replace window and the Auto-replace panel.</summary>
public static class MultipleReplaceEngine
{
    /// <summary>Builds one expression (regex rules are compiled into <paramref name="regexCache"/>). Null for an empty find or invalid regex.</summary>
    public static ReplaceExpression? Make(string find, string replaceWith, MultipleReplaceType type, string ruleInfo, Dictionary<string, Regex> regexCache)
    {
        if (string.IsNullOrEmpty(find))
        {
            return null; // an empty find would never advance in Apply
        }

        var isRegex = type == MultipleReplaceType.RegularExpression;
        if (isRegex)
        {
            find = RegexUtils.FixNewLine(find);
            replaceWith = RegexUtils.FixNewLine(replaceWith);
            if (!regexCache.ContainsKey(find))
            {
                try
                {
                    regexCache.Add(find, new Regex(find, RegexOptions.Compiled | RegexOptions.Multiline));
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }
        }

        var searchType = type switch
        {
            MultipleReplaceType.RegularExpression => ReplaceExpression.SearchTypeRegularExpression,
            MultipleReplaceType.CaseSensitive => ReplaceExpression.SearchTypeCaseSensitive,
            _ => ReplaceExpression.SearchTypeNormal,
        };

        return new ReplaceExpression(find, replaceWith, searchType, ruleInfo);
    }

    /// <summary>Applies the expressions in order. Expressions that matched are added to <paramref name="hits"/>.</summary>
    public static string Apply(string text, IEnumerable<ReplaceExpression> expressions, Dictionary<string, Regex> regexCache, List<ReplaceExpression>? hits = null)
    {
        foreach (var item in expressions)
        {
            if (item.SearchType == ReplaceExpression.SearchCaseSensitive)
            {
                if (text.Contains(item.FindWhat))
                {
                    hits?.Add(item);
                    text = text.Replace(item.FindWhat, item.ReplaceWith);
                }
            }
            else if (item.SearchType == ReplaceExpression.SearchRegEx)
            {
                var r = regexCache[item.FindWhat];
                if (r.IsMatch(text))
                {
                    hits?.Add(item);
                    text = RegexUtils.ReplaceNewLineSafe(r, text, item.ReplaceWith);
                }
            }
            else
            {
                var index = text.IndexOf(item.FindWhat, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    hits?.Add(item);
                    do
                    {
                        text = text.Remove(index, item.FindWhat.Length).Insert(index, item.ReplaceWith);
                        index = text.IndexOf(item.FindWhat, index + item.ReplaceWith.Length, StringComparison.OrdinalIgnoreCase);
                    } while (index >= 0);
                }
            }
        }

        return text;
    }
}
