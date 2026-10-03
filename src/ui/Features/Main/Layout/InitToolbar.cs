using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Project;
using System.Linq;
using System.Windows.Input;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

public static class InitToolbar
{
    public static Border Make(MainViewModel vm)
    {
        return new Border
        {
            Child = UiUtil.MakeSideScroller(CreateToolbar(vm)), // scrolls instead of running under the window edge
        };
    }

    /// <param name="Show">The user's toolbar setting for this button (Options → Toolbar).</param>
    /// <param name="VisiblePath">Only for this subtitle format (a view-model bool), e.g. ASSA tools.</param>
    private sealed record Item(bool Show, string Glyph, string Hint, ICommand Command, string CommandName, string? VisiblePath = null);

    private static StackPanel CreateToolbar(MainViewModel vm)
    {
        var a = Se.Settings.Appearance;
        var h = Se.Language.Main.Toolbar;
        var assa = nameof(vm.IsFormatAssa);
        var ssa = nameof(vm.IsFormatSsa);

        // groups, with a separator between groups that show anything
        Item[][] groups =
        [
            [
                new(a.ToolbarShowFileNew, Glyphs.New, h.NewHint, vm.CommandFileNewCommand, nameof(vm.CommandFileNewCommand)),
                new(a.ToolbarShowFileOpen, Glyphs.FolderOpen, h.OpenHint, vm.CommandFileOpenCommand, nameof(vm.CommandFileOpenCommand)),
                new(a.ToolbarShowVideoFileOpen, Glyphs.Video, h.OpenVideoHint, vm.CommandVideoOpenCommand, nameof(vm.CommandVideoOpenCommand)),
                new(a.ToolbarShowSave, Glyphs.Save, h.SaveHint, vm.CommandFileSaveCommand, nameof(vm.CommandFileSaveCommand)),
                new(a.ToolbarShowSaveAs, Glyphs.SaveAs, h.SaveAsHint, vm.CommandFileSaveAsCommand, nameof(vm.CommandFileSaveAsCommand)),
            ],
            [
                new(a.ToolbarShowFind, Glyphs.Find, h.FindHint, vm.ShowFindCommand, nameof(vm.ShowFindCommand)),
                new(a.ToolbarShowReplace, Glyphs.FindReplace, h.ReplaceHint, vm.ShowReplaceCommand, nameof(vm.ShowReplaceCommand)),
            ],
            [
                new(a.ToolbarShowSpellCheck, Glyphs.Spellcheck, h.SpellCheckHint, vm.ShowSpellCheckCommand, nameof(vm.ShowSpellCheckCommand)),
                new(a.ToolbarShowFixCommonErrors, Glyphs.Shield, h.FixCommonErrorsHint, vm.ShowToolsFixCommonErrorsCommand, nameof(vm.ShowToolsFixCommonErrorsCommand)),
                new(a.ToolbarShowRemoveTextForHi, Glyphs.ClosedCaption, h.RemoveTextForHiHint, vm.ShowToolsRemoveTextForHearingImpairedCommand, nameof(vm.ShowToolsRemoveTextForHearingImpairedCommand)),
                new(a.ToolbarShowVisualSync, Glyphs.Sync, h.VisualSyncHint, vm.ShowVisualSyncCommand, nameof(vm.ShowVisualSyncCommand)),
                new(a.ToolbarShowBeautifyTimeCodes, Glyphs.Effects, h.BeautifyTimeCodesHint, vm.ShowBeautifyTimeCodesCommand, nameof(vm.ShowBeautifyTimeCodesCommand)),
                new(a.ToolbarShowBurnIn, Glyphs.BurnIn, h.BurnInHint, vm.ShowVideoBurnInCommand, nameof(vm.ShowVideoBurnInCommand)),
                new(a.ToolbarShowSettings, Glyphs.Settings, h.SettingsHint, vm.CommandShowSettingsCommand, nameof(vm.CommandShowSettingsCommand)),
                new(a.ToolbarShowLayout, Glyphs.Layout, h.LayoutHint, vm.CommandShowLayoutCommand, nameof(vm.CommandShowLayoutCommand)),
            ],
            [
                new(a.ToolbarShowHelp, Glyphs.Help, h.HelpHint, vm.ShowHelpCommand, nameof(vm.ShowHelpCommand)),
            ],
            [
                new(true, Glyphs.Styles, h.AssaStylesHint, vm.ShowAssaStylesCommand, nameof(vm.ShowAssaStylesCommand), assa),
                new(true, Glyphs.ScriptInfo, h.AssaPropertiesHint, vm.ShowAssaPropertiesCommand, nameof(vm.ShowAssaPropertiesCommand), assa),
                new(true, Glyphs.Attachments, h.AssaAttachmentsHint, vm.ShowAssaAttachmentsCommand, nameof(vm.ShowAssaAttachmentsCommand), assa),
                new(true, Glyphs.Draw, h.AssaDrawHint, vm.ShowAssaDrawCommand, nameof(vm.ShowAssaDrawCommand), assa),
                new(true, Glyphs.Styles, h.AssaStylesHint, vm.ShowSsaStylesCommand, nameof(vm.ShowSsaStylesCommand), ssa),
                new(true, Glyphs.ScriptInfo, h.AssaPropertiesHint, vm.ShowSsaPropertiesCommand, nameof(vm.ShowSsaPropertiesCommand), ssa),
                new(true, Glyphs.Attachments, h.AssaAttachmentsHint, vm.ShowSsaAttachmentsCommand, nameof(vm.ShowSsaAttachmentsCommand), ssa),
            ],
        ];

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Margin = new Thickness(2),
            DataContext = vm,
        };
        var shortcuts = ShortcutsMain.GetUsedShortcuts(vm);
        foreach (var group in groups)
        {
            var shown = group.Where(i => i.Show).ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            if (panel.Children.Count > 0)
            {
                // the format-only group's separator shows with its buttons
                var separator = MakeSeparator();
                if (shown.All(i => i.VisiblePath != null))
                {
                    separator.Bind(Visual.IsVisibleProperty, new Binding(nameof(vm.HasFormatStyle)));
                }

                panel.Children.Add(separator);
            }

            foreach (var item in shown)
            {
                var button = new Button
                {
                    Content = Glyphs.Make(item.Glyph),
                    Command = item.Command,
                    [AutomationProperties.NameProperty] = item.Hint,
                    [ToolTip.TipProperty] = UiUtil.MakeToolTip(item.Hint, shortcuts, item.CommandName),
                };
                if (item.VisiblePath != null)
                {
                    button.Bind(Visual.IsVisibleProperty, new Binding(item.VisiblePath));
                }

                ChromeStyles.AsTool(button);
                panel.Children.Add(button);
            }
        }

        return panel;
    }

    /// <summary>
    /// Episode / format / encoding / frame-rate pickers. They live in the menu row (right side), so they
    /// stay reachable when the icon toolbar is hidden. Rebuilt with the toolbar.
    /// </summary>
    public static StackPanel MakePickers(MainViewModel vm)
    {
        var appearance = Se.Settings.Appearance;
        var stackPanelRight = new StackPanel
        {
            Classes = { "se-pickers" },
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // project episodes (only while a project is open)
        stackPanelRight.Children.Add(new TextBlock
        {
            Classes = { "se-caption" },
            Text = Se.Language.Project.Episode,
            VerticalAlignment = VerticalAlignment.Center,
            DataContext = vm,
            [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsProjectOpen)),
        });
        stackPanelRight.Children.Add(new ComboBox
        {
            Width = 240,
            Margin = new Thickness(0, 0, 8, 0),
            [AutomationProperties.NameProperty] = Se.Language.Project.Episode,
            [!ComboBox.ItemsSourceProperty] = new Binding(nameof(vm.ProjectEpisodes)),
            [!ComboBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedProjectEpisode)) { Mode = BindingMode.TwoWay },
            [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsProjectOpen)),
            DisplayMemberBinding = new Binding(nameof(ProjectEpisode.Display)),
            DataContext = vm,
        });

        // subtitle formats
        stackPanelRight.Children.Add(new TextBlock
        {
            Classes = { "se-caption" },
            Text = Se.Language.General.Format,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var comboBoxSubtitleFormat = new ComboBox
        {
            Width = 240,
            [AutomationProperties.NameProperty] = Se.Language.General.Format,
            [!ComboBox.ItemsSourceProperty] = new Binding(nameof(vm.SubtitleFormats)),
            [!ComboBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedSubtitleFormat)),
            DataContext = vm,
            ItemTemplate = new FuncDataTemplate<object>((item, _) =>
                new TextBlock
                {
                    [!TextBlock.TextProperty] = new Binding(nameof(SubtitleFormat.Name)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                }, true)
        };
        comboBoxSubtitleFormat.SelectionChanged += vm.ComboBoxSubtitleFormatChanged;
        comboBoxSubtitleFormat.KeyDown += vm.ComboBoxSubtitleFormatKeyDown;
        // Tunnel phase so we see the event before ComboBox consumes a left-click to open
        // its dropdown (matters for Mac Ctrl+Click, which Avalonia delivers as left+Ctrl).
        comboBoxSubtitleFormat.AddHandler(InputElement.PointerPressedEvent,
            vm.ComboBoxSubtitleFormatPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        stackPanelRight.Children.Add(comboBoxSubtitleFormat);

        if (appearance.ToolbarShowEncoding)
        {
            stackPanelRight.Children.Add(new TextBlock
            {
                Classes = { "se-caption" },
                Text = Se.Language.General.Encoding,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            var comboBoxEncoding = new ComboBox
            {
                Width = 200,
                [AutomationProperties.NameProperty] = Se.Language.General.Encoding,
                [!ComboBox.ItemsSourceProperty] = new Binding(nameof(vm.Encodings)),
                [!ComboBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedEncoding)),
                DataContext = vm,
            };
            stackPanelRight.Children.Add(comboBoxEncoding);
        }

        if (appearance.ToolbarShowFrameRate)
        {
            stackPanelRight.Children.Add(new TextBlock
            {
                Classes = { "se-caption" },
                Text = Se.Language.General.FrameRate,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            });
            var comboBoxFrameRate = new ComboBox
            {
                Width = 110,
                [AutomationProperties.NameProperty] = Se.Language.General.FrameRate,
                [!ComboBox.ItemsSourceProperty] = new Binding(nameof(vm.FrameRates)),
                [!ComboBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedFrameRate)),
                DataContext = vm,
            };
            stackPanelRight.Children.Add(comboBoxFrameRate);
            comboBoxFrameRate.SelectionChanged += vm.ComboBoxFrameRateSelectionChanged;
        }

        return stackPanelRight;
    }

    private static Border MakeSeparator()
    {
        return new Border
        {
            Classes = { "se-sep" },
            Width = 1,
            Margin = new Thickness(5, 8, 5, 8),
        };
    }
}