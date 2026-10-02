using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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

        // the wheel scrolls along the bar
        AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            var delta = (Math.Abs(e.Delta.Y) > Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X) * 40;
            Offset = Orientation == Orientation.Horizontal
                ? Offset.WithX(Math.Clamp(Offset.X - delta, 0, Math.Max(0, Extent.Width - Viewport.Width)))
                : Offset.WithY(Math.Clamp(Offset.Y - delta, 0, Math.Max(0, Extent.Height - Viewport.Height)));
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

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

    /// <summary>A context button with a line icon (path data on the 24x24 grid, see <see cref="Glyphs"/>), like the tool buttons.</summary>
    public static Button ContextButton(string glyph, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Classes = { "se-tool" },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Focusable = false, // keys keep going to the drawing
            Content = Glyph(glyph),
            [AutomationProperties.NameProperty] = tooltip,
            [ToolTip.TipProperty] = tooltip,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    public static ToggleButton ContextToggle(string glyph, string tooltip, bool isChecked, Action<bool> onChanged)
    {
        var button = new ToggleButton
        {
            Classes = { "se-tool" },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            IsChecked = isChecked,
            Focusable = false, // keys keep going to the drawing
            Content = Glyph(glyph),
            [AutomationProperties.NameProperty] = tooltip,
            [ToolTip.TipProperty] = tooltip,
        };
        button.IsCheckedChanged += (_, _) => onChanged(button.IsChecked == true);
        return button;
    }

    /// <summary>Fills a context button's line icon (e.g. the colour swatch circle).</summary>
    public static void SetGlyphFill(Button button, Color color)
    {
        if (button.Content is Viewbox { Child: Canvas canvas } && canvas.Children.Count > 0 && canvas.Children[0] is Path path)
        {
            path.Fill = new SolidColorBrush(color);
        }
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
                children.Add(new Button
                {
                    Classes = { "se-tool" },
                    Width = 32,
                    Height = 32,
                    Padding = new Thickness(0),
                    Command = tool.Command(_vm),
                    Content = Glyph(tool.Glyph),
                    [AutomationProperties.NameProperty] = title,
                    [ToolTip.TipProperty] = UiUtil.MakeToolTip(title, shortcuts, tool.CommandName),
                    [!IsEnabledProperty] = new Binding(nameof(MainViewModel.IsFormatAssa)) { Source = _vm },
                });
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

    private static Control Glyph(string data) => new Viewbox
    {
        Width = 20,
        Height = 20,
        Child = new Canvas
        {
            Width = 24,
            Height = 24,
            Children =
            {
                new Path
                {
                    Data = Geometry.Parse(data),
                    StrokeThickness = 1.6,
                    StrokeLineCap = PenLineCap.Round,
                    StrokeJoin = PenLineJoin.Round,
                },
            },
        },
    };

    internal static class Glyphs
    {
        // ---- context controls (drawing on the video) ----

        // Same rules as the tools below: live area ~3.5-20.5, rounded corners r2, a frame + 2-3 strokes at most.

        // An arrow hooking back: undo the last point (tip on the same 4.5 edge as the grid frame).
        public const string Undo = "M9 4.5 L4.5 9 L9 13.5 M4.5 9 H14.5 A5 5 0 0 1 14.5 19 H10";

        // A 2x2 grid in a rounded square (frame + 2 lines, the density of Script info / Background).
        public const string Grid = "M6.5 4.5 H17.5 A2 2 0 0 1 19.5 6.5 V17.5 A2 2 0 0 1 17.5 19.5 H6.5 A2 2 0 0 1 4.5 17.5 V6.5 A2 2 0 0 1 6.5 4.5 Z M12 4.5 V19.5 M4.5 12 H19.5";

        // Corner brackets (arms like Motion's reticle) around a frame (corners like Resolution): fit the frame in view.
        public const string Fit = "M3.5 7.5 V5 H6 M18 5 H20.5 V7.5 M20.5 16.5 V19 H18 M6 19 H3.5 V16.5 M8.5 8.5 H15.5 A1.5 1.5 0 0 1 17 10 V14 A1.5 1.5 0 0 1 15.5 15.5 H8.5 A1.5 1.5 0 0 1 7 14 V10 A1.5 1.5 0 0 1 8.5 8.5 Z";

        // A rounded colour chip the drawing colour fills (SetGlyphFill); smaller than the outlines so it weighs the same.
        public const string Swatch = "M8.5 6.5 H15.5 A2 2 0 0 1 17.5 8.5 V15.5 A2 2 0 0 1 15.5 17.5 H8.5 A2 2 0 0 1 6.5 15.5 V8.5 A2 2 0 0 1 8.5 6.5 Z";

        // Check / cross in a ring (like Position's): the same weight as the enclosed tool glyphs, and as each other.
        public const string Done = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M8.5 12 L11 14.5 L15.5 10";

        public const string Cancel = "M12 3.5 A8.5 8.5 0 1 0 12 20.5 A8.5 8.5 0 1 0 12 3.5 Z M8.5 8.5 L15.5 15.5 M15.5 8.5 L8.5 15.5";

        // ---- tools ----

        // "Aa": a style is a named look for text.
        public const string Styles = "M3.5 18 L8 6 L12.5 18 M5.2 14 H10.8 M20 12.5 V18 M17.2 12.4 A2.8 2.8 0 1 0 17.2 18 A2.8 2.8 0 1 0 17.2 12.4";

        // A sheet of settings: [Script Info].
        public const string ScriptInfo = "M6.5 3.5 H17.5 A2 2 0 0 1 19.5 5.5 V18.5 A2 2 0 0 1 17.5 20.5 H6.5 A2 2 0 0 1 4.5 18.5 V5.5 A2 2 0 0 1 6.5 3.5 Z M8.5 8.5 H15.5 M8.5 12 H15.5 M8.5 15.5 H12.5";

        // Paperclip: embedded fonts and graphics.
        public const string Attachments = "M15 7 L8.6 13.4 A2 2 0 0 0 11.4 16.2 L18.3 9.3 A4 4 0 0 0 12.6 3.6 L5.7 10.5 A6 6 0 0 0 14.2 19 L19.5 13.7";

        // A 16:9 frame with a scale arrow: PlayRes / resampling.
        public const string Resolution = "M4 6.5 H20 A1.5 1.5 0 0 1 21.5 8 V16 A1.5 1.5 0 0 1 20 17.5 H4 A1.5 1.5 0 0 1 2.5 16 V8 A1.5 1.5 0 0 1 4 6.5 Z M8.5 14.5 L15.5 9.5 M12 9.5 H15.5 V13";

        // Crosshair: \pos.
        public const string Position = "M12 6 A6 6 0 1 0 12 18 A6 6 0 1 0 12 6 Z M12 2.5 V6 M12 18 V21.5 M2.5 12 H6 M18 12 H21.5 M12 11 A1 1 0 1 0 12 13 A1 1 0 1 0 12 11";

        // A bezier with anchors and handles: \p drawings.
        public const string Draw = "M5 18.5 C7 11 13 8 19 5.5 M5 18.5 L7 11 M19 5.5 L13 8 M3.5 17 H6.5 V20 H3.5 Z M17.5 4 H20.5 V7 H17.5 Z M7 9.9 A1.1 1.1 0 1 0 7 12.1 A1.1 1.1 0 1 0 7 9.9 M13 6.9 A1.1 1.1 0 1 0 13 9.1 A1.1 1.1 0 1 0 13 6.9";

        // Text lines on a plate: background box behind a line.
        public const string Background = "M5 6 H19 A2 2 0 0 1 21 8 V16 A2 2 0 0 1 19 18 H5 A2 2 0 0 1 3 16 V8 A2 2 0 0 1 5 6 Z M7 10.5 H17 M7 13.5 H13.5";

        // A tracking reticle (corner brackets + centre dot) at the end of a dashed motion path.
        public const string Motion = "M13.5 6 V3.5 H16 M18 3.5 H20.5 V6 M20.5 8 V10.5 H18 M16 10.5 H13.5 V8 M17 6.2 A0.8 0.8 0 1 0 17 7.8 A0.8 0.8 0 1 0 17 6.2 M3.8 20 L5.3 18.7 M7.2 17.2 L8.7 15.9 M10.6 14.4 L12.1 13.1";

        // An opacity ramp: a wedge with hatching, rising (in) or falling (out).
        public const string FadeIn = "M3.5 19 L20.5 5 V19 Z M9 19 V14.5 M14.5 19 V9.9";
        public const string FadeOut = "M3.5 5 L20.5 19 H3.5 Z M9.5 9.9 V19 M15 14.5 V19";

        // {\}: an override tag block.
        public const string OverrideTags = "M8.5 4 C6.5 4 6.5 5.5 6.5 7.5 C6.5 9.5 5.5 11 4 12 C5.5 13 6.5 14.5 6.5 16.5 C6.5 18.5 6.5 20 8.5 20 M15.5 4 C17.5 4 17.5 5.5 17.5 7.5 C17.5 9.5 18.5 11 20 12 C18.5 13 17.5 14.5 17.5 16.5 C17.5 18.5 17.5 20 15.5 20 M10.2 8 L13.8 16";

        // Sparkles: advanced effects.
        public const string Effects = "M10 4 C10.6 8.4 11.6 9.4 16 10 C11.6 10.6 10.6 11.6 10 16 C9.4 11.6 8.4 10.6 4 10 C8.4 9.4 9.4 8.4 10 4 Z M17.5 14 C17.8 16.2 18.3 16.7 20.5 17 C18.3 17.3 17.8 17.8 17.5 20 C17.2 17.8 16.7 17.3 14.5 17 C16.7 16.7 17.2 16.2 17.5 14 Z";

        // Eyedropper: pick a color from the frame.
        public const string ColorPicker = "M13 7 L17 11 M17.8 3.9 A2.3 2.3 0 1 0 17.8 8.5 A2.3 2.3 0 1 0 17.8 3.9 M14.15 8.15 L6.65 15.65 L5.5 18.5 L8.35 17.35 L15.85 9.85";

        // A partly filled bar.
        public const string ProgressBar = "M6.5 9 H17.5 A3 3 0 0 1 17.5 15 H6.5 A3 3 0 0 1 6.5 9 Z M6.5 12 H12";
    }
}
