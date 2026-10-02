using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Edit.MultipleReplace;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Assa;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>Small entry points the dockable panels (Features\Main\Panels) use.</summary>
public partial class MainViewModel
{
    /// <summary>Current ASS/SSA header. A new string reference means it changed.</summary>
    internal string? SubtitleHeader => _subtitle?.Header;

    internal void SetScriptInfoValue(string key, string value)
    {
        var header = string.IsNullOrWhiteSpace(_subtitle.Header) ? AdvancedSubStationAlpha.DefaultHeader : _subtitle.Header;
        if (AssaScriptInfo.Get(header, key) == value)
        {
            return;
        }

        _subtitle.Header = AssaScriptInfo.Set(header, key, value);
        RefreshSubtitlePreview();
    }

    /// <summary>Applies replace rules to the selected (or all) lines as one undo step. Returns the number of changed lines; <paramref name="dryRun"/> only counts.</summary>
    internal int ApplyReplaceExpressions(IReadOnlyList<ReplaceExpression> expressions, Dictionary<string, Regex> regexCache, bool selectedOnly, bool dryRun)
    {
        var lines = selectedOnly ? SubtitleGrid.SelectedItems.Cast<SubtitleLineViewModel>().ToList() : Subtitles.ToList();
        var changes = lines
            .Select(line => (Line: line, Text: MultipleReplaceEngine.Apply(line.Text, expressions, regexCache)))
            .Where(c => c.Text != c.Line.Text)
            .ToList();

        if (dryRun || changes.Count == 0)
        {
            return changes.Count;
        }

        RunWithoutChangeDetection(() =>
        {
            foreach (var (line, text) in changes)
            {
                line.Text = text;
            }
        });

        RefreshSubtitlePreview();
        _updateAudioVisualizer = true;
        ShowStatus(string.Format(Se.Language.Edit.MultipleReplace.XLinesAffected, changes.Count));
        return changes.Count;
    }
}
