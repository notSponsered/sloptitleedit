using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Shared.ColorPicker;

/// <summary>
/// Spectrum on the left; new/original swatches, hex + ASS codes and RGBA sliders on the right; recent colors and
/// the screen eyedropper below (picks from anywhere, the video included; no second picker window).
/// </summary>
public class ColorPickerWindow : Window
{
    public ColorPickerWindow(ColorPickerViewModel vm)
    {
        UiUtil.InitializeWindow(this, GetType().Name);
        Title = vm.IsStandalone ? Se.Language.Assa.ImageColorPicker : Se.Language.Tools.ColorPickerTitle;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        vm.Window = this;
        DataContext = vm;

        var spectrum = new ColorSpectrum { Width = 288, MinHeight = 200 }; // stretches to the height of the controls beside it
        spectrum.SetColor(vm.SelectedColor);
        spectrum.ColorChanged += (_, color) => vm.UpdateFromSpectrum(color);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SelectedColor))
            {
                spectrum.SetColor(vm.SelectedColor);
            }
        };

        var controls = new StackPanel
        {
            Spacing = 8,
            Width = 290,
            Children = { MakeSwatches(vm), MakeCodes(vm), MakeSliders(vm) },
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Children = { spectrum, controls } };

        var pickButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Optris.Icons.Avalonia.Icon { Value = IconNames.EyeDropper, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = Se.Language.Tools.PickFromScreen, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Command = vm.PickFromScreenCommand,
            IsVisible = vm.CanPickFromScreen,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };

        var recentRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { MakeRecent(vm), pickButton },
        };
        Grid.SetColumn(pickButton, 1);

        var buttons = vm.IsStandalone
            ? UiUtil.MakeButtonBar(UiUtil.MakeButton(Se.Language.General.Close, vm.CancelCommand))
            : UiUtil.MakeButtonBar(UiUtil.MakeButtonOk(vm.OkCommand), UiUtil.MakeButtonCancel(vm.CancelCommand));

        Content = new StackPanel
        {
            Margin = UiUtil.MakeWindowMargin(),
            Spacing = 14,
            Children = { top, recentRow, buttons },
        };

        KeyDown += (_, e) => vm.OnKeyDown(e);
        if (vm.IsStandalone)
        {
            Opened += (_, _) => vm.PickFromScreenCommand.Execute(null); // the image color picker starts picking
        }
    }

    /// <summary>New (live) next to original (click to go back), on a checkerboard so alpha shows.</summary>
    private static Control MakeSwatches(ColorPickerViewModel vm)
    {
        Border Swatch(string property, CornerRadius radius) => new()
        {
            Height = 44,
            CornerRadius = radius,
            [!Border.BackgroundProperty] = new Binding(property) { Converter = new ColorToBrushConverter() },
        };

        var original = Swatch(nameof(vm.OriginalColor), new CornerRadius(0, 6, 6, 0));
        original.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(original, Se.Language.Tools.OriginalColor);
        AutomationProperties.SetName(original, Se.Language.Tools.OriginalColor);
        original.PointerPressed += (_, _) => vm.RestoreOriginalCommand.Execute(null);

        var newSwatch = Swatch(nameof(vm.SelectedColor), new CornerRadius(6, 0, 0, 6));
        ToolTip.SetTip(newSwatch, Se.Language.Tools.NewColor);
        Grid.SetColumn(original, 1);

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = UiUtil.GetBorderBrush(),
            Background = Checkerboard(),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*"), Children = { newSwatch, original } },
        };
    }

    private static Control MakeCodes(ColorPickerViewModel vm)
    {
        Control Row(string label, string property, int maxLength, CommunityToolkit.Mvvm.Input.IRelayCommand copy)
        {
            var box = new TextBox
            {
                MaxLength = maxLength,
                FontFeatures = FontFeatureCollection.Parse("tnum"),
                [!TextBox.TextProperty] = new Binding(property) { Mode = BindingMode.TwoWay },
            };
            AutomationProperties.SetName(box, label);
            var copyButton = UiUtil.MakeButton(copy, "mdi-content-copy");
            ToolTip.SetTip(copyButton, Se.Language.General.Copy);
            AutomationProperties.SetName(copyButton, $"{Se.Language.General.Copy} {label}");
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("34,*,Auto"),
                ColumnSpacing = 6,
                Children =
                {
                    new TextBlock { Text = label, Classes = { "se-caption" }, VerticalAlignment = VerticalAlignment.Center },
                    box,
                    copyButton,
                },
            };
            Grid.SetColumn(box, 1);
            Grid.SetColumn(copyButton, 2);
            return grid;
        }

        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Row("Hex", nameof(vm.HexColor), 9, vm.CopyHexCommand),
                Row("ASS", nameof(vm.AssColor), 12, vm.CopyAssCommand),
            },
        };
    }

    private static Control MakeSliders(ColorPickerViewModel vm)
    {
        ColorChannelSlider Channel(string label, string value, string start, string end) => new()
        {
            Label = label,
            [!ColorChannelSlider.ValueProperty] = new Binding(value) { Mode = BindingMode.TwoWay },
            [!ColorChannelSlider.StartColorProperty] = new Binding(start),
            [!ColorChannelSlider.EndColorProperty] = new Binding(end),
        };

        var alpha = Channel("A", nameof(vm.Alpha), nameof(vm.AlphaGradientStart), nameof(vm.AlphaGradientEnd));
        alpha.Bind(IsVisibleProperty, new Binding(nameof(vm.ShowAlpha)));
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Channel("R", nameof(vm.Red), nameof(vm.RedGradientStart), nameof(vm.RedGradientEnd)),
                Channel("G", nameof(vm.Green), nameof(vm.GreenGradientStart), nameof(vm.GreenGradientEnd)),
                Channel("B", nameof(vm.Blue), nameof(vm.BlueGradientStart), nameof(vm.BlueGradientEnd)),
                alpha,
            },
        };
    }

    private static Control MakeRecent(ColorPickerViewModel vm)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in vm.RecentColors)
        {
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = UiUtil.GetBorderBrush(),
                Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(swatch, hex);
            AutomationProperties.SetName(swatch, hex);
            swatch.PointerPressed += (_, _) => vm.PickRecent(color);
            row.Children.Add(swatch);
        }

        return new StackPanel
        {
            Spacing = 4,
            IsVisible = vm.RecentColors.Count > 0,
            Children = { new TextBlock { Text = Se.Language.Tools.RecentColors, Classes = { "se-caption" } }, row },
        };
    }

    /// <summary>Grey/white squares behind swatches, so transparency is visible.</summary>
    private static IBrush Checkerboard()
    {
        var light = new SolidColorBrush(Color.FromRgb(204, 204, 204));
        var dark = new SolidColorBrush(Color.FromRgb(153, 153, 153));
        return new DrawingBrush
        {
            TileMode = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 12, 12, RelativeUnit.Absolute),
            Drawing = new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing { Brush = light, Geometry = new RectangleGeometry(new Rect(0, 0, 12, 12)) },
                    new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(0, 0, 6, 6)) },
                    new GeometryDrawing { Brush = dark, Geometry = new RectangleGeometry(new Rect(6, 6, 6, 6)) },
                },
            },
        };
    }
}
