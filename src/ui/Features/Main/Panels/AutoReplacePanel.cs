using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Edit.MultipleReplace;
using Nikse.SubtitleEdit.Logic.Config;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>
/// Docked Multiple replace: the same rules (Edit → Multiple replace) with checkboxes, a live hit
/// count, and apply to selected/all lines as one undo step.
/// </summary>
internal static class AutoReplacePanel
{
    public static Control Make(MainViewModel vm)
    {
        var l = Se.Language.Workspace;
        var rules = new StackPanel { Margin = new Thickness(8, 6), Spacing = 2 };
        var info = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8, Margin = new Thickness(0, 0, 8, 0) };
        var regexCache = new Dictionary<string, Regex>();

        List<ReplaceExpression> Build()
        {
            var list = new List<ReplaceExpression>();
            foreach (var category in Se.Settings.Edit.MultipleReplace.Categories.Where(c => c.IsActive))
            {
                foreach (var rule in category.Rules.Where(r => r.Active))
                {
                    if (MultipleReplaceEngine.Make(rule.Find, rule.ReplaceWith, rule.Type, category.Name, regexCache) is { } expression)
                    {
                        list.Add(expression);
                    }
                }
            }

            return list;
        }

        void UpdateCount() =>
            info.Text = string.Format(Se.Language.Edit.MultipleReplace.XLinesAffected, vm.ApplyReplaceExpressions(Build(), regexCache, false, dryRun: true));

        void Fill()
        {
            rules.Children.Clear();
            foreach (var category in Se.Settings.Edit.MultipleReplace.Categories)
            {
                var categoryBox = new CheckBox { Content = category.Name, IsChecked = category.IsActive, FontWeight = FontWeight.SemiBold };
                categoryBox.IsCheckedChanged += (_, _) =>
                {
                    category.IsActive = categoryBox.IsChecked == true;
                    UpdateCount();
                };
                rules.Children.Add(categoryBox);

                foreach (var rule in category.Rules)
                {
                    var ruleBox = new CheckBox { Content = $"{rule.Find}  →  {rule.ReplaceWith}", IsChecked = rule.Active, Margin = new Thickness(20, 0, 0, 0) };
                    if (!string.IsNullOrEmpty(rule.Description))
                    {
                        ToolTip.SetTip(ruleBox, rule.Description);
                    }

                    ruleBox.IsCheckedChanged += (_, _) =>
                    {
                        rule.Active = ruleBox.IsChecked == true;
                        UpdateCount();
                    };
                    rules.Children.Add(ruleBox);
                }
            }

            UpdateCount();
        }

        var applySelected = new Button { Content = l.ApplyToSelectedLines };
        applySelected.Click += (_, _) =>
        {
            vm.ApplyReplaceExpressions(Build(), regexCache, true, dryRun: false);
            UpdateCount();
        };

        var applyAll = new Button { Content = l.ApplyToAllLines, Margin = new Thickness(6, 0, 0, 0) };
        applyAll.Click += (_, _) =>
        {
            vm.ApplyReplaceExpressions(Build(), regexCache, false, dryRun: false);
            UpdateCount();
        };

        var editRules = new Button { Content = l.EditRules, Margin = new Thickness(6, 0, 0, 0) };
        editRules.Click += async (_, _) =>
        {
            await vm.ShowMultipleReplaceCommand.ExecuteAsync(null);
            Fill(); // the window saves rule changes to the same settings
        };

        var bottom = new WrapPanel { Margin = new Thickness(8, 6), Children = { info, applySelected, applyAll, editRules } };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var root = new DockPanel { Children = { bottom, new ScrollViewer { Content = rules } } };
        root.AttachedToVisualTree += (_, _) => Fill();
        return root;
    }
}
