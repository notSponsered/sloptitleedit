using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Windows.Input;

namespace Nikse.SubtitleEdit.Features.Main.Panels;

/// <summary>
/// The ASSA tools as a dockable toolbar: one custom line icon per tool, grouped by what the tool
/// works on. One row (docked top/bottom) or column (docked left/right) that scrolls when it doesn't fit.
/// While a tool is active its options ("context controls", <see cref="MainViewModel.ContextControls"/>) are added at the end.
/// </summary>
internal sealed class AssaToolbar : ScrollViewer
{
    private readonly StackPanel _panel = new() { VerticalAlignment = VerticalAlignment.Center };

    private Orientation Orientation
    {
        get => _panel.Orientation;
        set
        {
            _panel.Orientation = value;
            // Hidden, not Auto: the theme's scroll bar floats over the content and would swallow clicks on the
            // lower part of the buttons; the mouse wheel scrolls the bar instead
            HorizontalScrollBarVisibility = value == Orientation.Horizontal ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Disabled;
            VerticalScrollBarVisibility = value == Orientation.Horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Hidden;
        }
    }

    private sealed record Tool(Func<string> Title, string Glyph, Func<MainViewModel, ICommand> Command, string CommandName);

    // Icons are drawn on a 24×24 grid as strokes (round caps/joins), so they share one weight.
    private static readonly Tool[][] Groups =
    [
        [ // the script itself
            new(() => Se.Language.Main.Menu.AssaStyles, Glyphs.Styles, vm => vm.ShowAssaStylesCommand, nameof(MainViewModel.ShowAssaStylesCommand)),
            new(() => Se.Language.Main.Menu.AssaProperties, Glyphs.ScriptInfo, vm => vm.ShowAssaPropertiesCommand, nameof(MainViewModel.ShowAssaPropertiesCommand)),
            new(() => Se.Language.Main.Menu.AssaAttachments, Glyphs.Attachments, vm => vm.ShowAssaAttachmentsCommand, nameof(MainViewModel.ShowAssaAttachmentsCommand)),
            new(() => Se.Language.Main.Menu.AssaChangeResolution, Glyphs.Resolution, vm => vm.ShowAssaChangeResolutionCommand, nameof(MainViewModel.ShowAssaChangeResolutionCommand)),
        ],
        [ // placing and drawing on the frame
            new(() => Se.Language.Main.Menu.AssaSetPosition, Glyphs.Position, vm => vm.ShowAssaSetPositionCommand, nameof(MainViewModel.ShowAssaSetPositionCommand)),
            new(() => Se.Language.Main.Menu.AssaDraw, Glyphs.Draw, vm => vm.ShowAssaDrawCommand, nameof(MainViewModel.ShowAssaDrawCommand)),
            new(() => Se.Language.Main.Menu.AssaGenerateBackground, Glyphs.Background, vm => vm.ShowAssaGenerateBackgroundCommand, nameof(MainViewModel.ShowAssaGenerateBackgroundCommand)),
            new(() => Se.Language.Main.Menu.AssaMotionTracking, Glyphs.Motion, vm => vm.ShowAssaMotionTrackingCommand, nameof(MainViewModel.ShowAssaMotionTrackingCommand)),
            new(() => Se.Language.Main.Menu.AssaMotionShapes, Glyphs.MotionShapes, vm => vm.ShowAssaMotionShapesCommand, nameof(MainViewModel.ShowAssaMotionShapesCommand)),
        ],
        [ // timing and effects
            new(() => Se.Language.Main.Menu.AssaFadeInToVideoPosition, Glyphs.FadeIn, vm => vm.AssaFadeInToVideoPositionCommand, nameof(MainViewModel.AssaFadeInToVideoPositionCommand)),
            new(() => Se.Language.Main.Menu.AssaFadeOutFromVideoPosition, Glyphs.FadeOut, vm => vm.AssaFadeOutFromVideoPositionCommand, nameof(MainViewModel.AssaFadeOutFromVideoPositionCommand)),
            new(() => Se.Language.Main.Menu.AssaApplyCustomOverrideTags, Glyphs.OverrideTags, vm => vm.ShowAssaApplyCustomOverrideTagsCommand, nameof(MainViewModel.ShowAssaApplyCustomOverrideTagsCommand)),
            new(() => Se.Language.Main.Menu.AssaApplyAdvancedEffects, Glyphs.Effects, vm => vm.ShowAssaApplyAdvancedEffectCommand, nameof(MainViewModel.ShowAssaApplyAdvancedEffectCommand)),
        ],
        [ // color and generators
            new(() => Se.Language.Main.Menu.AssaImageColorPicker, Glyphs.ColorPicker, vm => vm.ShowAssaImageColorPickerCommand, nameof(MainViewModel.ShowAssaImageColorPickerCommand)),
            new(() => Se.Language.Main.Menu.AssaProgressBar, Glyphs.ProgressBar, vm => vm.ShowAssaGenerateProgressBarCommand, nameof(MainViewModel.ShowAssaGenerateProgressBarCommand)),
        ],
    ];

    private readonly MainViewModel _vm;

    public AssaToolbar(MainViewModel vm)
    {
        _vm = vm;
        VerticalAlignment = VerticalAlignment.Center;
        Content = _panel;
        Orientation = Orientation.Horizontal;
        Build();

        UiUtil.WheelScrollsAlong(this);

        AttachedToVisualTree += (_, _) =>
        {
            _vm.ContextControls.CollectionChanged += OnContextControlsChanged;
            Build();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _vm.ContextControls.CollectionChanged -= OnContextControlsChanged;
            _panel.Children.Clear(); // release the context controls so another bar can show them
        };
    }

    private void OnContextControlsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Build();

    /// <summary>A context button with a line icon (see <see cref="Glyphs"/>), like the tool buttons.</summary>
    public static Button ContextButton(string glyph, string tooltip, Action onClick)
    {
        var button = MakeTool(new Button(), glyph, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    public static ToggleButton ContextToggle(string glyph, string tooltip, bool isChecked, Action<bool> onChanged)
    {
        var button = MakeTool(new ToggleButton { IsChecked = isChecked }, glyph, tooltip);
        button.IsCheckedChanged += (_, _) => onChanged(button.IsChecked == true);
        return button;
    }

    private static T MakeTool<T>(T button, string glyph, string tooltip) where T : Button
    {
        button.Content = Glyphs.Make(glyph);
        button.Focusable = false; // keys keep going to the drawing
        AutomationProperties.SetName(button, tooltip);
        ToolTip.SetTip(button, tooltip);
        ChromeStyles.AsTool(button);
        return button;
    }

    /// <summary>Docked left/right the tools stack in columns; top/bottom in rows.</summary>
    public void SetOrientation(Orientation orientation)
    {
        if (Orientation != orientation)
        {
            Orientation = orientation;
            Build();
        }
    }

    private Border MakeSeparator()
    {
        var horizontal = Orientation == Orientation.Horizontal;
        return new Border
        {
            Classes = { "se-sep" },
            Width = horizontal ? 1 : 18,
            Height = horizontal ? 18 : 1,
            Margin = horizontal ? new Thickness(6, 7) : new Thickness(7, 6),
        };
    }

    private void Build()
    {
        var children = _panel.Children;
        children.Clear();
        var shortcuts = ShortcutsMain.GetUsedShortcuts(_vm);
        for (var g = 0; g < Groups.Length; g++)
        {
            if (g > 0)
            {
                children.Add(MakeSeparator());
            }

            foreach (var tool in Groups[g])
            {
                var title = tool.Title().Replace("_", string.Empty).Replace("...", string.Empty);
                var button = new Button
                {
                    Command = tool.Command(_vm),
                    Content = Glyphs.Make(tool.Glyph),
                    [AutomationProperties.NameProperty] = title,
                    [ToolTip.TipProperty] = UiUtil.MakeToolTip(title, shortcuts, tool.CommandName),
                    [!IsEnabledProperty] = new Binding(nameof(MainViewModel.IsFormatAssa)) { Source = _vm },
                };
                ChromeStyles.AsTool(button);
                children.Add(button);
            }
        }

        if (!IsShown() || _vm.ContextControls.Count == 0)
        {
            return;
        }

        // the active tool's options
        children.Add(MakeSeparator());
        foreach (var control in _vm.ContextControls)
        {
            (control.Parent as Panel)?.Children.Remove(control); // e.g. still in a bar that is being replaced
            children.Add(control);
        }

        // show them: scroll to the end of the bar
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Offset = new Vector(Extent.Width, Extent.Height), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private bool IsShown() => TopLevel.GetTopLevel(this) != null;
}
