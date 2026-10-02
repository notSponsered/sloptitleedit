using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Nikse.SubtitleEdit.Features.Assa;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Assa;
using System;
using System.Globalization;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>
/// The [Script Info] values typesetters touch most. PlayRes is read-only here on purpose: changing it
/// without resampling moves everything, so it goes through "Change resolution" (which resamples).
/// </summary>
internal static class ScriptInfoPanel
{
    private static readonly string[] Matrices = ["None", "TV.601", "TV.709", "PC.601", "PC.709", "TV.FCC", "PC.FCC", "TV.240M", "PC.240M"];

    public static Control Make(MainViewModel vm)
    {
        var l = Se.Language.Workspace;
        var updating = false;

        var title = new TextBox { MinWidth = 160 };
        void CommitTitle()
        {
            if (!updating && title.Text != null)
            {
                vm.SetScriptInfoValue("Title", title.Text.Trim());
            }
        }

        title.LostFocus += (_, _) => CommitTitle();
        title.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitTitle();
                e.Handled = true;
            }
        };

        var playRes = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var changeResolution = new Button { Content = l.ChangeResolution, Command = vm.ShowAssaChangeResolutionCommand, Margin = new Thickness(8, 0, 0, 0) };

        var scaled = new CheckBox();
        scaled.IsCheckedChanged += (_, _) =>
        {
            if (!updating)
            {
                vm.SetScriptInfoValue("ScaledBorderAndShadow", scaled.IsChecked == true ? "yes" : "no");
            }
        };

        var wrapStyles = WrapStyleItem.List();
        var wrap = new ComboBox { ItemsSource = wrapStyles, MinWidth = 160 };
        wrap.SelectionChanged += (_, _) =>
        {
            if (!updating && wrap.SelectedItem is WrapStyleItem item)
            {
                vm.SetScriptInfoValue("WrapStyle", ((int)item.Style).ToString(CultureInfo.InvariantCulture));
            }
        };

        var matrix = new ComboBox { ItemsSource = Matrices, MinWidth = 160 };
        matrix.SelectionChanged += (_, _) =>
        {
            if (!updating && matrix.SelectedItem is string value)
            {
                vm.SetScriptInfoValue("YCbCr Matrix", value);
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
            Margin = new Thickness(10),
            RowSpacing = 8,
            ColumnSpacing = 10,
        };
        AddRow(grid, 0, Se.Language.General.Title, title);
        AddRow(grid, 1, Se.Language.General.Resolution, new StackPanel { Orientation = Orientation.Horizontal, Children = { playRes, changeResolution } });
        AddRow(grid, 2, l.ScaledBorderAndShadow, scaled);
        AddRow(grid, 3, Se.Language.Assa.WrapStyle, wrap);
        AddRow(grid, 4, l.YCbCrMatrix, matrix);
        var more = new Button { Content = l.MoreProperties, Command = vm.ShowAssaPropertiesCommand };
        Grid.SetRow(more, 5);
        Grid.SetColumn(more, 1);
        grid.Children.Add(more);

        var content = new ScrollViewer { Content = grid };
        var placeholder = PanelUtil.AssaOnlyPlaceholder();
        var root = new Grid { Children = { content, placeholder } };

        string? lastHeader = null;
        PanelUtil.PollWhileVisible(root, () =>
        {
            var isAss = PanelUtil.IsAssOrSsa(vm);
            content.IsVisible = isAss;
            placeholder.IsVisible = !isAss;
            var header = vm.SubtitleHeader;
            if (!isAss || ReferenceEquals(header, lastHeader) || title.IsFocused)
            {
                return;
            }

            lastHeader = header;
            updating = true;
            try
            {
                title.Text = AssaScriptInfo.Get(header, "Title") ?? string.Empty;
                playRes.Text = $"{AssaScriptInfo.Get(header, "PlayResX") ?? "?"} × {AssaScriptInfo.Get(header, "PlayResY") ?? "?"}";
                scaled.IsChecked = string.Equals(AssaScriptInfo.Get(header, "ScaledBorderAndShadow"), "yes", StringComparison.OrdinalIgnoreCase);
                var wrapValue = AssaScriptInfo.Get(header, "WrapStyle");
                wrap.SelectedItem = wrapStyles.FirstOrDefault(w => ((int)w.Style).ToString(CultureInfo.InvariantCulture) == wrapValue);
                var matrixValue = AssaScriptInfo.Get(header, "YCbCr Matrix");
                matrix.SelectedItem = Matrices.FirstOrDefault(m => m.Equals(matrixValue, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                updating = false;
            }
        });

        return root;
    }

    private static void AddRow(Grid grid, int row, string label, Control editor)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        editor.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(text);
        grid.Children.Add(editor);
    }
}
