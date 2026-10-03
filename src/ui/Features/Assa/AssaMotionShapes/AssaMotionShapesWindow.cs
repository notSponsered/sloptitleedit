using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Assa.AssaDraw;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.ValueConverters;

namespace Nikse.SubtitleEdit.Features.Assa.AssaMotionShapes;

/// <summary>
/// Picture + timeline + transport on the left (like a video editor), a sidebar of labelled cards on the right
/// (Shape, Keys, Calculate) in the order they're used, status and Apply/Cancel at the bottom.
/// </summary>
public class AssaMotionShapesWindow : Window
{
    private static readonly IBrush CardBackground = new SolidColorBrush(Colors.Gray, 0.12); // reads on light and dark themes

    public AssaMotionShapesWindow(AssaMotionShapesViewModel vm)
    {
        var l = Se.Language.Assa;
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = l.MotionShapes;
        Width = 1200;
        Height = 820;
        MinWidth = 850;
        MinHeight = 560;
        CanResize = true;
        vm.Window = this;
        DataContext = vm;

        var canvas = new VideoDrawCanvas();
        canvas.Changed += (_, _) => vm.OnCanvasChanged();
        canvas[!InputElement.IsHitTestVisibleProperty] = new Binding(nameof(vm.IsBusy)) { Converter = new InverseBooleanConverter() }; // no edits while calculating
        vm.Canvas = canvas;
        var timeline = new ShapeTimeline();
        timeline.Seek += vm.Seek;
        vm.Timeline = timeline;

        // buttons never take focus, so Space/Enter go to play/the canvas instead of pressing the last clicked button again
        var notBusy = new InverseBooleanConverter();
        T NotFocusable<T>(T button, bool whenBusy = false) where T : Button
        {
            button.Focusable = false;
            button[!IsEnabledProperty] = whenBusy ? new Binding(nameof(vm.IsBusy)) : new Binding(nameof(vm.IsBusy)) { Converter = notBusy };
            return button;
        }

        Button TextButton(string text, IRelayCommand command, string? tip = null)
        {
            var button = NotFocusable(UiUtil.MakeButton(text, command));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (tip != null)
            {
                ToolTip.SetTip(button, tip);
            }

            return button;
        }

        Button IconButton(string icon, IRelayCommand command, string tip) => NotFocusable(UiUtil.MakeButton(command, icon, tip));

        // ---- left: picture, timeline, transport
        var transport = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto"), ColumnSpacing = 4 };
        AddAt(transport, IconButton(Glyphs.Play, vm.PlayCommand, l.ShapesPlay + " (Space)"), 0);
        AddAt(transport, IconButton(Glyphs.SkipPrevious, vm.FirstFrameCommand, "|◀"), 1);
        AddAt(transport, IconButton(Glyphs.ChevronLeft, vm.PreviousFrameCommand, "◀ (←)"), 2);
        AddAt(transport, new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFeatures = FontFeatureCollection.Parse("tnum"),
            [!TextBlock.TextProperty] = new Binding(nameof(vm.FrameInfo)),
        }, 3);
        AddAt(transport, IconButton(Glyphs.ChevronRight, vm.NextFrameCommand, "▶ (→)"), 4);
        AddAt(transport, IconButton(Glyphs.PlayNext, vm.LastFrameCommand, "▶|"), 5);

        var left = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 6 };
        left.Children.Add(canvas);
        Grid.SetRow(timeline, 1);
        left.Children.Add(timeline);
        Grid.SetRow(transport, 2);
        left.Children.Add(transport);

        // ---- right: Shape
        var nameBox = new TextBox
        {
            [!TextBox.TextProperty] = new Binding(nameof(vm.StyleName)) { Mode = BindingMode.OneWay },
            [Avalonia.Automation.AutomationProperties.NameProperty] = l.DrawOnVideoName,
        };
        nameBox.LostFocus += (_, _) => vm.CommitStyleName(nameBox.Text);
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                if (e.Key == Key.Enter)
                {
                    vm.CommitStyleName(nameBox.Text);
                }
                else
                {
                    nameBox.Text = vm.StyleName;
                }

                canvas.Focus();
                e.Handled = true;
            }
        };

        var colorButton = new Button
        {
            Focusable = false,
            Command = vm.PickColorCommand,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new Border
            {
                Width = 40,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                [!Border.BackgroundProperty] = new Binding(nameof(vm.StyleBrush)),
            },
            [ToolTip.TipProperty] = l.DrawOnVideoColor,
        };

        var simplifySlider = new Slider
        {
            Minimum = 0,
            Maximum = 4,
            SmallChange = 0.25,
            LargeChange = 1,
            TickFrequency = 0.25,
            IsSnapToTickEnabled = true,
            Focusable = false,
            [!Slider.ValueProperty] = new Binding(nameof(vm.Simplify)) { Mode = BindingMode.TwoWay },
            [ToolTip.TipProperty] = l.ShapesSimplify,
        };
        var simplifyRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        simplifyRow.Children.Add(simplifySlider);
        AddAt(simplifyRow, new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Width = 44,
            TextAlignment = TextAlignment.Right,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.Simplify)) { StringFormat = "{0:0.##} px" },
        }, 1);

        var shapeCard = Card(l.ShapesSectionShape, null,
            Row(new TextBlock { Text = l.DrawOnVideoName, VerticalAlignment = VerticalAlignment.Center, Width = 60 }, nameBox),
            Row(new TextBlock { Text = l.DrawOnVideoColor, VerticalAlignment = VerticalAlignment.Center, Width = 60 }, colorButton),
            Row(new TextBlock { Text = l.ShapesSimplify, VerticalAlignment = VerticalAlignment.Center, Width = 60 }, simplifyRow));

        // ---- right: Keys
        var keyButtons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 6 };
        AddAt(keyButtons, TextButton("◆ " + l.ShapesAddKey, vm.AddKeyCommand, l.ShapesAddKey + " (K)"), 0);
        AddAt(keyButtons, TextButton(l.ShapesDeleteKey, vm.DeleteKeyCommand), 1);
        var previousKey = TextButton("◀◆", vm.PreviousKeyCommand, l.ShapesPreviousKey + " (Alt+←)");
        var nextKey = TextButton("◆▶", vm.NextKeyCommand, l.ShapesNextKey + " (Alt+→)");
        Grid.SetRow(previousKey, 1);
        Grid.SetRow(nextKey, 1);
        AddAt(keyButtons, previousKey, 0);
        AddAt(keyButtons, nextKey, 1);
        var keysCard = Card(l.ShapesSectionKeys, new TextBlock { Opacity = 0.7, [!TextBlock.TextProperty] = new Binding(nameof(vm.KeyCount)) }, keyButtons);

        // ---- right: Calculate
        CheckBox MakeCheckBox(string text, string property) => new()
        {
            Content = text,
            Focusable = false,
            [!CheckBox.IsCheckedProperty] = new Binding(property) { Mode = BindingMode.TwoWay },
            [!IsEnabledProperty] = new Binding(nameof(vm.IsBusy)) { Converter = notBusy },
        };

        var calculate = TextButton(l.ShapesCalculate, vm.CalculateCommand, l.ShapesCalculate + " (F5)");
        calculate.Classes.Add("accent");
        calculate.Padding = new Thickness(0, 7);
        var busy = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6, [!IsVisibleProperty] = new Binding(nameof(vm.IsBusy)) };
        busy.Children.Add(new ProgressBar { IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center });
        AddAt(busy, NotFocusable(UiUtil.MakeButton(l.MotionStop, vm.StopCommand), whenBusy: true), 1);
        var calculateCard = Card(l.ShapesCalculate, null,
            MakeCheckBox(l.ShapesFollowMotion, nameof(vm.FollowMotion)),
            MakeCheckBox(l.ShapesTrackColor, nameof(vm.TrackColor)),
            calculate,
            busy,
            new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                [!TextBlock.TextProperty] = new Binding(nameof(vm.Stats)),
            });

        var sidebar = new ScrollViewer
        {
            Width = 270,
            Margin = new Thickness(10, 0, 0, 0),
            Content = new StackPanel { Spacing = 10, Children = { shapeCard, keysCard, calculateCard } },
        };

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        content.Children.Add(left);
        AddAt(content, sidebar, 1);

        // ---- bottom: what to do / what happened, and the dialog buttons
        var apply = NotFocusable(UiUtil.MakeButton(l.ShapesApply, vm.ApplyCommand));
        apply.Classes.Add("accent");
        var cancel = UiUtil.MakeButtonCancel(vm.CancelCommand);
        cancel.Focusable = false;
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        bottom.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.Status)),
        });
        AddAt(bottom, UiUtil.MakeButtonBar(apply, cancel), 1);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 10,
            Margin = UiUtil.MakeWindowMargin(),
        };
        root.Children.Add(content);
        Grid.SetRow(bottom, 1);
        root.Children.Add(bottom);
        Content = root;

        Loaded += (_, _) =>
        {
            vm.OnLoaded();
            canvas.Focus();
        };
        Closing += (_, _) => vm.OnClosing();
        AddHandler(KeyDownEvent, (_, e) => vm.OnKeyDown(e), RoutingStrategies.Tunnel);
    }

    /// <summary>A titled group in the sidebar (optional info text right of the title).</summary>
    private static Border Card(string title, Control? info, params Control[] children)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 2) };
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold });
        if (info != null)
        {
            info.VerticalAlignment = VerticalAlignment.Center;
            AddAt(header, info, 1);
        }

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(header);
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new Border { Background = CardBackground, CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 10), Child = panel };
    }

    /// <summary>Label (or fixed part) left, the rest stretched.</summary>
    private static Grid Row(Control first, Control rest)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        grid.Children.Add(first);
        AddAt(grid, rest, 1);
        return grid;
    }

    private static void AddAt(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
