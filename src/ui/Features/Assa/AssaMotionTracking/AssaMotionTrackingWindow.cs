using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.ValueConverters;

namespace Nikse.SubtitleEdit.Features.Assa.AssaMotionTracking;

public class AssaMotionTrackingWindow : Window
{
    public AssaMotionTrackingWindow(AssaMotionTrackingViewModel vm)
    {
        var l = Se.Language.Assa;
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = l.MotionTracking;
        Width = 1150;
        Height = 780;
        MinWidth = 800;
        MinHeight = 550;
        CanResize = true;
        vm.Window = this;
        DataContext = vm;

        var canvas = new MotionTrackCanvas();
        canvas.BoxesChanged += (_, _) => vm.OnBoxesChanged();
        canvas.SelectionChanged += (_, _) => vm.OnCanvasSelectionChanged();
        vm.Canvas = canvas;

        // toolbar
        var notBusy = new InverseBooleanConverter();
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                UiUtil.MakeButton("◀ " + l.MotionTrackBackward, vm.TrackBackwardCommand).WithBindEnabled(nameof(vm.IsBusy), notBusy),
                UiUtil.MakeButton(l.MotionTrackForward + " ▶", vm.TrackForwardCommand).WithBindEnabled(nameof(vm.IsBusy), notBusy),
                UiUtil.MakeButton(l.MotionStop, vm.StopCommand).WithBindEnabled(nameof(vm.IsBusy)),
                new CheckBox
                {
                    Content = l.MotionTrackScaleAndRotation,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    [!CheckBox.IsCheckedProperty] = new Binding(nameof(vm.TrackScaleAndRotation)) { Mode = BindingMode.TwoWay },
                },
                new CheckBox
                {
                    Content = l.MotionShowPreview,
                    VerticalAlignment = VerticalAlignment.Center,
                    [!CheckBox.IsCheckedProperty] = new Binding(nameof(vm.ShowSubtitlePreview)) { Mode = BindingMode.TwoWay },
                },
                new ProgressBar
                {
                    Width = 200,
                    Minimum = 0,
                    Maximum = 100,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                    [!ProgressBar.ValueProperty] = new Binding(nameof(vm.Progress)),
                    [!ProgressBar.IsVisibleProperty] = new Binding(nameof(vm.IsBusy)),
                },
            },
        };

        // right panel: tracking points, recorded tracks, reference frame
        var pointList = new ListBox
        {
            Height = 150,
            [!ListBox.ItemsSourceProperty] = new Binding(nameof(vm.Points)),
            [!ListBox.SelectedIndexProperty] = new Binding(nameof(vm.SelectedPointIndex)) { Mode = BindingMode.TwoWay },
        };
        var trackList = new ListBox
        {
            Height = 150,
            [!ListBox.ItemsSourceProperty] = new Binding(nameof(vm.Tracks)),
            [!ListBox.SelectedItemProperty] = new Binding(nameof(vm.SelectedTrack)) { Mode = BindingMode.TwoWay },
        };
        var rightPanel = new StackPanel
        {
            Spacing = 6,
            Width = 250,
            Margin = new Thickness(10, 0, 0, 0),
            Children =
            {
                UiUtil.MakeTextBlock(l.MotionPoints),
                pointList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        UiUtil.MakeButton(l.MotionAddPoint, vm.AddPointCommand).WithBindEnabled(nameof(vm.IsBusy), notBusy),
                        UiUtil.MakeButton(l.MotionRemovePoint, vm.RemovePointCommand).WithBindEnabled(nameof(vm.IsBusy), notBusy),
                    },
                },
                new TextBlock { Text = l.MotionRecordedTracks, Margin = new Thickness(0, 14, 0, 0) },
                trackList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        UiUtil.MakeButton(l.MotionNewTrack, vm.NewTrackCommand),
                        UiUtil.MakeButton(l.MotionDeleteTrack, vm.DeleteTrackCommand),
                    },
                },
                new TextBlock
                {
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(0, 20, 0, 0),
                    [!TextBlock.TextProperty] = new Binding(nameof(vm.ReferenceInfo)),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        UiUtil.MakeButton(l.MotionUseThisFrame, vm.UseThisFrameCommand),
                        UiUtil.MakeButton(l.MotionGoToReferenceFrame, vm.GoToReferenceFrameCommand),
                    },
                },
            },
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        content.Children.Add(canvas);
        Grid.SetColumn(rightPanel, 1);
        content.Children.Add(rightPanel);

        // frame navigation
        var slider = new Slider
        {
            Minimum = 0,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            [!Slider.MaximumProperty] = new Binding(nameof(vm.MaxFrameIndex)),
            [!Slider.ValueProperty] = new Binding(nameof(vm.FrameIndex)) { Mode = BindingMode.TwoWay },
        };
        var navigation = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto"),
            ColumnSpacing = 5,
        };
        navigation.Children.Add(UiUtil.MakeButton("|◀", vm.FirstFrameCommand));
        AddAt(navigation, UiUtil.MakeButton("◀", vm.PreviousFrameCommand), 1);
        AddAt(navigation, slider, 2);
        AddAt(navigation, UiUtil.MakeButton("▶", vm.NextFrameCommand), 3);
        AddAt(navigation, UiUtil.MakeButton("▶|", vm.LastFrameCommand), 4);
        AddAt(navigation, new TextBlock
        {
            MinWidth = 260,
            VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.FrameInfo)),
        }, 5);

        // status + buttons
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        bottom.Children.Add(new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.Status)),
        });
        AddAt(bottom, UiUtil.MakeButtonBar(
            UiUtil.MakeButton(l.MotionApplyToSelectedLines, vm.ApplyCommand).WithBindEnabled(nameof(vm.IsBusy), notBusy),
            UiUtil.MakeButtonCancel(vm.CancelCommand)), 1);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            RowSpacing = 8,
            Margin = UiUtil.MakeWindowMargin(),
        };
        root.Children.Add(toolbar);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        Grid.SetRow(navigation, 2);
        root.Children.Add(navigation);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);
        Content = root;

        Loaded += (_, _) => vm.OnLoaded();
        Closing += (_, _) => vm.OnClosing();
        KeyDown += (_, e) => vm.OnKeyDown(e);
    }

    private static void AddAt(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
