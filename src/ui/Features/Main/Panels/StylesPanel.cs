using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>Style list of the current ASS/SSA file. Double-click (or the button) sets the style on the selected lines.</summary>
internal static class StylesPanel
{
    public static Control Make(MainViewModel vm)
    {
        var l = Se.Language.Workspace;
        var list = new ListBox { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent };

        void ApplySelected()
        {
            if (list.SelectedItem is ListBoxItem { Tag: string name })
            {
                vm.SetStyleForSelectedLinesCommand.Execute(name);
            }
        }

        list.DoubleTapped += (_, _) => ApplySelected();
        var setButton = new Button { Content = l.SetOnSelectedLines };
        setButton.Click += (_, _) => ApplySelected();

        var buttons = new WrapPanel
        {
            Margin = new Thickness(6),
            Children = { setButton, new Button { Content = l.EditStyles, Command = vm.ShowAssaStylesCommand, Margin = new Thickness(6, 0, 0, 0) } },
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var content = new DockPanel { Children = { buttons, list } };
        var placeholder = PanelUtil.AssaOnlyPlaceholder();
        var root = new Grid { Children = { content, placeholder } };

        string? lastHeader = null;
        string? lastStyle = null;
        object? lastLine = null;
        PanelUtil.PollWhileVisible(root, () =>
        {
            var isAss = PanelUtil.IsAssOrSsa(vm);
            content.IsVisible = isAss;
            placeholder.IsVisible = !isAss;
            if (!isAss)
            {
                return;
            }

            var rebuilt = false;
            var header = vm.SubtitleHeader;
            if (!ReferenceEquals(header, lastHeader))
            {
                lastHeader = header;
                Fill(list, header);
                rebuilt = true;
            }

            // Follow the current line's style, but only when it changes so the user's own pick isn't overridden.
            var line = vm.SelectedSubtitle;
            var style = line?.Style;
            if (rebuilt || style != lastStyle || line != lastLine)
            {
                lastStyle = style;
                lastLine = line;
                list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string?)i.Tag == style);
            }
        });

        return root;
    }

    private static void Fill(ListBox list, string? header)
    {
        list.Items.Clear();
        if (string.IsNullOrEmpty(header))
        {
            return;
        }

        foreach (var style in AdvancedSubStationAlpha.GetSsaStylesFromHeader(header))
        {
            list.Items.Add(new ListBoxItem
            {
                Tag = style.Name,
                Padding = new Thickness(6, 3),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Border
                        {
                            Width = 14,
                            Height = 14,
                            CornerRadius = new CornerRadius(2),
                            Background = new SolidColorBrush(style.Primary.ToAvaloniaColor()),
                            BorderBrush = UiUtil.GetBorderBrush(),
                            BorderThickness = new Thickness(1),
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new TextBlock { Text = style.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{style.FontName} {style.FontSize:0.#}", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            });
        }
    }
}
