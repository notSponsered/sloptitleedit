using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nikse.SubtitleEdit.Controls.AudioVisualizerControl;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using MenuItem = Avalonia.Controls.MenuItem;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

public class NonSpaceButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }
}

public class InitWaveform
{
    public static Grid MakeWaveform(MainViewModel vm)
    {
        var languageHints = Se.Language.Main.Waveform;
        var settings = Se.Settings.Waveform;
        var shortcuts = ShortcutsMain.GetUsedShortcuts(vm);

        // Create main layout grid
        var mainGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            Height = double.NaN, // Auto height
        };

        // waveform area
        if (vm.AudioVisualizer == null)
        {
            vm.AudioVisualizer = new AudioVisualizer
            {
                DrawGridLines = settings.DrawGridLines,
                InvertMouseWheel = settings.InvertMouseWheel,
                VerticalAlignment = VerticalAlignment.Stretch,
                Height = double.NaN, // Auto height
                WaveformDrawStyle = GetWaveformDrawStyle(settings.WaveformDrawStyle),
                FocusOnMouseOver = settings.FocusOnMouseOver,
                IsReadOnly = Se.Settings.General.LockTimeCodes,
                WaveformHeightPercentage = settings.SpectrogramCombinedWaveformHeight,
            };
            WaveformColors.ApplyTo(vm.AudioVisualizer);
            vm.AudioVisualizer.OnNewSelectionInsert += vm.AudioVisualizerOnNewSelectionInsert;
            vm.AudioVisualizer.OnVideoPositionChanged += vm.AudioVisualizerOnVideoPositionChanged;
            vm.AudioVisualizer.OnToggleSelection += vm.AudioVisualizerOnToggleSelection;
            //vm.AudioVisualizer.OnParagraphDoubleTapped += vm.OnWaveformDoubleTapped;
            vm.AudioVisualizer.OnPrimarySingleClicked += vm.AudioVisualizerOnPrimarySingleClicked;
            vm.AudioVisualizer.OnPrimaryDoubleClicked += vm.AudioVisualizerOnPrimaryDoubleClicked;
            vm.AudioVisualizer.OnDeletePressed += vm.AudioVisualizerOnDeletePressed;
            vm.AudioVisualizer.PointerReleased += vm.ControlMacPointerReleased;
            vm.AudioVisualizer.OnSelectRequested += vm.AudioVisualizerSelectRequested;
            vm.AudioVisualizer.OnSetStartAndOffsetTheRest += vm.AudioVisualizerSetStartAndOffsetTheRest;
            vm.AudioVisualizer.OnRazorCut += vm.AudioVisualizerRazorCut;
            vm.AudioVisualizer.RazorMode = vm.WaveformRazor;

            // Create a Flyout for the DataGrid
            var flyout = new MenuFlyout();
            vm.AudioVisualizer.FlyoutMenuOpening += vm.AudioVisualizerFlyoutMenuOpening;

            var insertSelectionMenuItem = new MenuItem
            {
                Header = Se.Language.General.InsertNewSelection,
                Command = vm.WaveformInsertNewSelectionCommand,
            };
            flyout.Items.Add(insertSelectionMenuItem);
            vm.MenuItemAudioVisualizerInsertNewSelection = insertSelectionMenuItem;

            var pasteSelectionMenuItem = new MenuItem
            {
                Header = Se.Language.General.PasteNewSelection,
                Command = vm.WaveformNewSelectionPasteFromClipboardCommand,
            };
            flyout.Items.Add(pasteSelectionMenuItem);
            vm.MenuItemAudioVisualizerPasteNewSelection = pasteSelectionMenuItem;

            var speechToTextNewSelectionMenuItem = new MenuItem
            {
                Header = Se.Language.Waveform.SpeechToTextNewSelectionDotDotDot,
                Command = vm.WaveformSpeechToTextNewSelectionCommand,
            };
            flyout.Items.Add(speechToTextNewSelectionMenuItem);
            vm.MenuItemAudioVisualizerSpeechToTextNewSelection = speechToTextNewSelectionMenuItem;

            var insertNewMenuItem = new MenuItem
            {
                Header = Se.Language.General.InsertAtPositionAndFocusTextBox,
                Command = vm.WaveformInsertAtPositionAndFocusTextBoxCommand,
            };
            flyout.Items.Add(insertNewMenuItem);
            vm.MenuItemAudioVisualizerInsertAtPosition = insertNewMenuItem;

            var pasteFromClipboardMenuItem = new MenuItem
            {
                Header = Se.Language.General.WaveformPasteFromClipboard,
                Command = vm.WaveformNewSelectionPasteFromClipboardCommand,
            };
            flyout.Items.Add(pasteFromClipboardMenuItem);
            vm.MenuItemAudioVisualizerPasteFromClipboardMenuItem = pasteFromClipboardMenuItem;

            var insertSubtitleFileAtPositionMenuItem = new MenuItem
            {
                Header = Se.Language.General.InsertSubtitleFileAtVideoPositionDotDotDot,
                Command = vm.InsertSubtitleFileAtVideoPositionCommand,
            };
            flyout.Items.Add(insertSubtitleFileAtPositionMenuItem);
            vm.MenuIteminsertSubtitleFileAtPositionMenuItem = insertSubtitleFileAtPositionMenuItem;

            var deleteAtPositionMenuItem = new MenuItem
            {
                Header = Se.Language.General.DeleteAtPosition,
                Command = vm.WaveformDeleteAtPositionCommand,
            };
            flyout.Items.Add(deleteAtPositionMenuItem);
            vm.MenuItemAudioVisualizerDeleteAtPosition = deleteAtPositionMenuItem;

            // Add menu items with commands
            var deleteMenuItem = new MenuItem
            {
                Header = Se.Language.General.Delete,
                Command = vm.DeleteSelectedLinesCommand
            };
            flyout.Items.Add(deleteMenuItem);
            vm.MenuItemAudioVisualizerDelete = deleteMenuItem;

            var insertBeforeMenuItem = new MenuItem
            {
                Header = Se.Language.General.InsertBefore,
                Command = vm.InsertLineBeforeCommand
            };
            flyout.Items.Add(insertBeforeMenuItem);
            vm.MenuItemAudioVisualizerInsertBefore = insertBeforeMenuItem;

            var insertAfterMenuItem = new MenuItem
            {
                Header = Se.Language.General.InsertAfter,
                Command = vm.InsertLineAfterCommand
            };
            flyout.Items.Add(insertAfterMenuItem);
            vm.MenuItemAudioVisualizerInsertAfter = insertAfterMenuItem;

            var separator1 = new Separator();
            flyout.Items.Add(separator1);
            vm.MenuItemAudioVisualizerSeparator1 = separator1;

            var splitMenuItem = new MenuItem
            {
                Header = Se.Language.General.SplitLine,
                Command = vm.SplitCommand,
            };
            flyout.Items.Add(splitMenuItem);
            vm.MenuItemAudioVisualizerSplit = splitMenuItem;

            var splitAtPositionMenuItem = new MenuItem
            {
                Header = Se.Language.General.SplitLineAtWaveformPosition,
                Command = vm.SplitAtPositionInWaveformCommand,
            };
            flyout.Items.Add(splitAtPositionMenuItem);
            vm.MenuItemAudioVisualizerSplitAtPosition = splitAtPositionMenuItem;

            flyout.Items.Add(new MenuItem
            {
                Header = languageHints.RazorAtVideoPosition,
                Command = vm.RazorAtVideoPositionCommand,
                [!Visual.IsVisibleProperty] = splitAtPositionMenuItem[!Visual.IsVisibleProperty], // shown when a line is under the playhead
            });

            var MergeWithPreviousMenuItem = new MenuItem
            {
                Header = Se.Language.General.MergeBefore,
                Command = vm.MergeWithLineBeforeCommand,
            };
            flyout.Items.Add(MergeWithPreviousMenuItem);
            vm.MenuItemAudioVisualizerMergeWithPrevious = MergeWithPreviousMenuItem;

            var MergeWithNextMenuItem = new MenuItem
            {
                Header = Se.Language.General.MergeAfter,
                Command = vm.MergeWithLineAfterCommand,
            };
            flyout.Items.Add(MergeWithNextMenuItem);
            vm.MenuItemAudioVisualizerMergeWithNext = MergeWithNextMenuItem;

            var fadeInMenuItem = new MenuItem
            {
                Header = Se.Language.Assa.FadeInToHere,
                Command = vm.AssaFadeInToVideoPositionCommand,
            };
            flyout.Items.Add(fadeInMenuItem);
            vm.MenuItemAudioVisualizerFadeIn = fadeInMenuItem;

            var fadeOutMenuItem = new MenuItem
            {
                Header = Se.Language.Assa.FadeOutFromHere,
                Command = vm.AssaFadeOutFromVideoPositionCommand,
            };
            flyout.Items.Add(fadeOutMenuItem);
            vm.MenuItemAudioVisualizerFadeOut = fadeOutMenuItem;

            flyout.Items.Add(new Separator());

            var menuItemFilterByLayer = new MenuItem
            {
                Header = Se.Language.General.FilterByLayer,
                Command = vm.ShowPickLayerFilterCommand,
            }.BindIsVisible(vm, nameof(vm.IsFormatAssa));
            flyout.Items.Add(menuItemFilterByLayer);

            var menuItemGuessTimeCodes = new MenuItem
            {
                Header = Se.Language.Waveform.GuessTimeCodesDotDotDot,
                Command = vm.ShowWaveformGuessTimeCodesCommand,
            };
            flyout.Items.Add(menuItemGuessTimeCodes);

            var menuItemAddShotChange = new MenuItem
            {
                Header = Se.Language.Waveform.ToggleShotChange,
                Command = vm.ToggleShotChangesAtVideoPositionCommand,
            };
            flyout.Items.Add(menuItemAddShotChange);

            var menuItemSeekSilence = new MenuItem
            {
                Header = Se.Language.Waveform.SeekSilenceDotDotDot,
                Command = vm.ShowWaveformSeekSilenceCommand,
            };
            flyout.Items.Add(menuItemSeekSilence);

            var menuItemExtractAudio = new MenuItem
            {
                Header = Se.Language.Waveform.ExtractAudioDotDotDot,
                Command = vm.WaveformExtractAudioCommand,
            };
            flyout.Items.Add(menuItemExtractAudio);
            vm.MenuItemAudioVisualizerExtractAudio = menuItemExtractAudio;

            var menuItemSpeechToTextSelectedLines = new MenuItem
            {
                Header = Se.Language.Waveform.SpeechToTextSelectedLinesDotDotDot,
                Command = vm.SpeechToTextSelectedLinesCommand,
            };
            flyout.Items.Add(menuItemSpeechToTextSelectedLines);
            vm.MenuItemAudioVisualizerSpeechToTextSelectedLines = menuItemSpeechToTextSelectedLines;

            var separatorDisplayMode = new Separator();
            separatorDisplayMode.DataContext = vm;
            separatorDisplayMode.Bind(Separator.IsVisibleProperty, new Binding(nameof(vm.ShowWaveformDisplayModeSeparator)));
            flyout.Items.Add(separatorDisplayMode);

            var showOnlyWaveformMenuItem = new MenuItem
            {
                Header = Se.Language.Waveform.ShowOnlyWaveform,
                Command = vm.WaveformShowOnlyWaveformCommand,
            }.BindIsVisible(vm, nameof(vm.ShowWaveformOnlyWaveform));
            flyout.Items.Add(showOnlyWaveformMenuItem);

            var showOnlySpectrogramMenuItem = new MenuItem
            {
                Header = Se.Language.Waveform.ShowOnlySpectrogram,
                Command = vm.WaveformShowOnlySpectrogramCommand,
            }.BindIsVisible(vm, nameof(vm.ShowWaveformOnlySpectrogram));
            flyout.Items.Add(showOnlySpectrogramMenuItem);

            var showWaveformAndSpectrogramMenuItem = new MenuItem
            {
                Header = Se.Language.Waveform.ShowWaveformAndSpectrogram,
                Command = vm.WaveformShowWaveformAndSpectrogramCommand,
            }.BindIsVisible(vm, nameof(vm.ShowWaveformWaveformAndSpectrogram));
            flyout.Items.Add(showWaveformAndSpectrogramMenuItem);

            vm.AudioVisualizer.MenuFlyout = flyout;
        }
        else
        {
            vm.AudioVisualizer.RemoveControlFromParent();
        }

        Grid.SetRow(vm.AudioVisualizer, 0);
        mainGrid.Children.Add(vm.AudioVisualizer);

        // Footer: the toolbar (the user's items in the user's order, Settings → Waveform toolbar) and the gear
        var items = new Dictionary<SeWaveformToolbarItemType, Control[]>();

        T Tool<T>(T button, SeWaveformToolbarItemType type, string glyph, string hint, string? commandName = null) where T : Button
        {
            var setting = GetToolbarSettingFor(type);
            button.Margin = new Thickness(setting.LeftMargin, 0, setting.RightMargin, 0);
            button.Content = Glyphs.Make(glyph);
            ToolTip.SetTip(button, UiUtil.MakeToolTip(hint, shortcuts, commandName ?? string.Empty));
            ChromeStyles.AsTool(button);
            items[type] = [button];
            return button;
        }

        Button Command(SeWaveformToolbarItemType type, string glyph, ICommand command, string hint, string commandName) =>
            Tool(new NonSpaceButton { Command = command }, type, glyph, hint, commandName);

        ToggleButton Toggle(SeWaveformToolbarItemType type, string glyph, string isCheckedPath, string hint, string? commandName = null) =>
            Tool(new ToggleButton { DataContext = vm, [!ToggleButton.IsCheckedProperty] = new Binding(isCheckedPath) { Mode = BindingMode.TwoWay } },
                type, glyph, hint, commandName);

        vm.ButtonWaveformPlay = Command(SeWaveformToolbarItemType.Play, Glyphs.Play, vm.TogglePlayPauseCommand, languageHints.PlayPauseHint, nameof(vm.TogglePlayPauseCommand));
        Command(SeWaveformToolbarItemType.PlaySelection, Glyphs.PlaySelection, vm.PlaySelectedLinesWithoutLoopCommand, languageHints.PlaySelectionHint, nameof(vm.PlaySelectedLinesWithoutLoopCommand));
        Command(SeWaveformToolbarItemType.Repeat, Glyphs.Repeat, vm.PlaySelectedLinesWithLoopCommand, languageHints.PlaySelectedRepeatHint, nameof(vm.PlaySelectedLinesWithLoopCommand));
        Command(SeWaveformToolbarItemType.PlayNext, Glyphs.PlayNext, vm.PlayNextCommand, languageHints.PlayNextHint, nameof(vm.PlayNextCommand));
        Command(SeWaveformToolbarItemType.New, Glyphs.Add, vm.WaveformInsertAtPositionAndFocusTextBoxCommand, languageHints.NewHint, nameof(vm.WaveformInsertAtPositionAndFocusTextBoxCommand));
        Command(SeWaveformToolbarItemType.SetStartAndOffsetTheRest, Glyphs.SetStartAndOffset, vm.WaveformSetStartAndOffsetTheRestCommand, languageHints.SetStartAndOffsetTheRestHint, nameof(vm.WaveformSetStartAndOffsetTheRestCommand));
        Command(SeWaveformToolbarItemType.SetStart, Glyphs.SetStart, vm.WaveformSetStartCommand, languageHints.SetStartHint, nameof(vm.WaveformSetStartCommand));
        Command(SeWaveformToolbarItemType.SetEnd, Glyphs.SetEnd, vm.WaveformSetEndCommand, languageHints.SetEndHint, nameof(vm.WaveformSetEndCommand));
        Command(SeWaveformToolbarItemType.RemoveBlankLines, Glyphs.RemoveBlankLines, vm.RemoveBlankLinesCommand, languageHints.RemoveBlankLines, nameof(vm.RemoveBlankLinesCommand));
        Toggle(SeWaveformToolbarItemType.AutoSelectOnPlay, Glyphs.FollowPlayback, nameof(vm.SelectCurrentSubtitleWhilePlaying), languageHints.SelectCurrentLineWhilePlayingHint)
            .IsCheckedChanged += (_, _) => vm.AutoSelectOnPlayCheckedChanged();
        Toggle(SeWaveformToolbarItemType.Center, Glyphs.Center, nameof(vm.WaveformCenter), languageHints.CenterWaveformHint)
            .IsCheckedChanged += (_, _) => vm.WaveformCenterCheckedChanged();
        Toggle(SeWaveformToolbarItemType.Snap, Glyphs.Snap, nameof(vm.WaveformSnap), languageHints.SnapHint, nameof(vm.ToggleWaveformSnapCommand));
        Toggle(SeWaveformToolbarItemType.Razor, Glyphs.Razor, nameof(vm.WaveformRazor), languageHints.RazorHint, nameof(vm.ToggleWaveformRazorCommand));

        var buttonMore = Tool(new NonSpaceButton(), SeWaveformToolbarItemType.More, Glyphs.More, Se.Language.General.More);
        buttonMore.Flyout = new MenuFlyout
        {
            Items =
            {
                new MenuItem
                {
                    Header = string.Format(languageHints.ResetZoomAndSpeed, UiUtil.MakeShortcutsString(shortcuts, nameof(vm.ResetWaveformZoomAndSpeedCommand))),
                    Command = vm.ResetWaveformZoomAndSpeedCommand,
                },
                new MenuItem
                {
                    Header = string.Format(languageHints.HideWaveformToolbar, string.Empty),
                    Command = vm.HideWaveformToolbarCommand,
                },
            },
        };

        // zoom: icon, slider and its percent side by side
        void Zoom(SeWaveformToolbarItemType type, string glyph, string valuePath, string hint)
        {
            var setting = GetToolbarSettingFor(type);
            var icon = Glyphs.Make(glyph, plain: true);
            icon.Margin = new Thickness(10, 0, 4, 0);
            var slider = new Slider
            {
                Minimum = 0.1,
                Maximum = 5.0,
                Width = 80,
                Value = 1,
                VerticalAlignment = VerticalAlignment.Center,
                [ToolTip.TipProperty] = UiUtil.MakeToolTip(hint, shortcuts),
                [!RangeBase.ValueProperty] = new Binding(nameof(vm.AudioVisualizer) + "." + valuePath),
            };
            var percent = new TextBlock
            {
                Classes = { "se-caption" },
                MinWidth = 34,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.TextProperty] = new Binding(nameof(vm.AudioVisualizer) + "." + valuePath)
                {
                    StringFormat = "{0:0}%",
                    Converter = new Avalonia.Data.Converters.FuncValueConverter<double, int>(v => (int)(v * 100)),
                },
            };
            items[type] =
            [
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(setting.LeftMargin, 0, setting.RightMargin, 0),
                    Children = { icon, slider, percent },
                },
            ];
        }

        Zoom(SeWaveformToolbarItemType.HorizontalZoom, Glyphs.ZoomHorizontal, nameof(vm.AudioVisualizer.ZoomFactor), languageHints.ZoomHorizontalHint);
        Zoom(SeWaveformToolbarItemType.VerticalZoom, Glyphs.ZoomVertical, nameof(vm.AudioVisualizer.VerticalZoomFactor), languageHints.ZoomVerticalHint);

        var settingPosition = GetToolbarSettingFor(SeWaveformToolbarItemType.VideoPositionSlider);
        var sliderPosition = new Slider
        {
            Minimum = 0,
            Width = 160,
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(settingPosition.LeftMargin, 0, settingPosition.RightMargin, 0),
            Focusable = true,
            [ToolTip.TipProperty] = UiUtil.MakeToolTip(languageHints.VideoPosition, shortcuts),
        };
        items[SeWaveformToolbarItemType.VideoPositionSlider] = [sliderPosition];

        if (vm.VideoPlayerControl != null)
        {
            sliderPosition.Bind(RangeBase.MaximumProperty, new Binding(nameof(vm.VideoPlayerControl) + "." + nameof(vm.VideoPlayerControl.Duration)));
            sliderPosition.Bind(RangeBase.ValueProperty, new Binding(nameof(vm.VideoPlayerControl) + "." + nameof(vm.VideoPlayerControl.Position)));
        }
        else
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var vp = vm.GetVideoPlayerControl(); // videoPlayerUndockedViewModel.VideoPlayerControl;
                    sliderPosition.DataContext = vp;
                    sliderPosition.Bind(RangeBase.MaximumProperty, new Binding(nameof(vp.Duration)));
                    sliderPosition.Bind(RangeBase.ValueProperty, new Binding(nameof(vp.Position)));
                });
            });
        }

        var sliderPositionUserMoving = false;
        sliderPosition.AddHandler(InputElement.PointerPressedEvent, (_, _) => sliderPositionUserMoving = true, RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(InputElement.PointerReleasedEvent, (_, _) => sliderPositionUserMoving = false, RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => sliderPositionUserMoving = false, RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            {
                sliderPositionUserMoving = true;
            }
        }, RoutingStrategies.Tunnel);
        sliderPosition.AddHandler(InputElement.KeyUpEvent, (_, _) => sliderPositionUserMoving = false, RoutingStrategies.Tunnel);
        sliderPosition.ValueChanged += (_, e) =>
        {
            if (!sliderPositionUserMoving)
            {
                return;
            }

            var av = vm.AudioVisualizer;
            if (av?.WavePeaks == null || av.Bounds.Width <= 0 || av.ZoomFactor <= 0 || av.WavePeaks.SampleRate <= 0)
            {
                return;
            }

            var halfWidthInSeconds = (av.Bounds.Width / 2) / (av.WavePeaks.SampleRate * av.ZoomFactor);
            av.StartPositionSeconds = e.NewValue - halfWidthInSeconds;
        };

        var settingSpeed = GetToolbarSettingFor(SeWaveformToolbarItemType.PlaybackSpeed);
        var comboBoxSpeed = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 10, 0),
            FontSize = 12,
            MinHeight = 24,
            Padding = new Thickness(4, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        comboBoxSpeed.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Speeds)));
        comboBoxSpeed.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(vm.SelectedSpeed)) { Mode = BindingMode.TwoWay });
        comboBoxSpeed.SelectionChanged += (s, e) =>
        {
            if (vm.AudioVisualizer != null && comboBoxSpeed.SelectedItem is string s1 && s1.EndsWith("x") &&
                double.TryParse(s1.Trim('x'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double speed))
            {
                vm.GetVideoPlayerControl()?.SetSpeed(speed);
            }
        };
        items[SeWaveformToolbarItemType.PlaybackSpeed] =
        [
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(settingSpeed.LeftMargin, 0, settingSpeed.RightMargin, 0),
                Children = { new TextBlock { Classes = { "se-caption" }, Text = Se.Language.General.Speed, VerticalAlignment = VerticalAlignment.Center }, comboBoxSpeed },
            },
        ];

        var controlsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var item in settings.ToolbarItems.Where(i => i.IsVisible).OrderBy(i => i.SortOrder))
        {
            controlsPanel.Children.AddRange(items.GetValueOrDefault(item.Type, []));
        }

        // Waveform settings (gear) at the bottom right
        var buttonSettings = new NonSpaceButton
        {
            Margin = new Thickness(4, 0, 0, 0),
            Content = Glyphs.Make(Glyphs.Settings),
            [ToolTip.TipProperty] = languageHints.WaveformSettings,
        };
        AutomationProperties.SetName(buttonSettings, languageHints.WaveformSettings);
        ChromeStyles.AsTool(buttonSettings);
        var flyoutSettings = new MenuFlyout();
        buttonSettings.Click += (_, _) =>
        {
            WaveformSettingsMenu.Fill(flyoutSettings, vm);
            flyoutSettings.ShowAt(buttonSettings);
        };

        // the toolbar stays centered, and scrolls sideways when the area is narrower instead of running under the gear
        var toolbar = UiUtil.MakeSideScroller(controlsPanel);
        toolbar.HorizontalAlignment = HorizontalAlignment.Center;
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { toolbar, buttonSettings },
            [!Visual.IsVisibleProperty] = new Binding(nameof(vm.IsWaveformToolbarVisible)),
        };
        Grid.SetColumn(buttonSettings, 1);
        mainGrid.Children.Add(footer);
        Grid.SetRow(footer, 1);

        DragDrop.SetAllowDrop(vm.AudioVisualizer, true);
        vm.AudioVisualizer.AddHandler(DragDrop.DragOverEvent, vm.VideoOnDragOver, RoutingStrategies.Bubble);
        vm.AudioVisualizer.AddHandler(DragDrop.DropEvent, vm.VideoOnDrop, RoutingStrategies.Bubble);

        return mainGrid;
    }

    private static SeWaveformToolbarItem GetToolbarSettingFor(SeWaveformToolbarItemType type)
    {
        var items = Se.Settings.Waveform.ToolbarItems;
        var item = items.FirstOrDefault(p => p.Type == type);
        if (item == null)
        {
            // An item added after this settings file was saved (e.g. Snap, Razor): start from its default.
            item = new SeWaveform().ToolbarItems.First(p => p.Type == type);
            items.Add(item);
        }

        return item;
    }

    public static WaveformDrawStyle GetWaveformDrawStyle(string waveformDrawStyle)
    {
        if (Enum.TryParse<WaveformDrawStyle>(waveformDrawStyle, ignoreCase: true, out var value))
        {
            return value;
        }

        return WaveformDrawStyle.Classic;
    }
}