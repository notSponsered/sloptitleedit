using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

public static class InitFooter
{
    public static Grid Make(MainViewModel vm)
    {
        // busy spinner | waveform progress | status text (trimmed, so it never pushes the right side away) | indicators
        var grid = new Grid
        {
            Classes = { "se-status" }, // muted 12 px text, tabular digits (ChromeStyles)
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(5, 0, 5, 0),
            DataContext = vm,
        };

        var waveformBusyIcon = Glyphs.Make(Glyphs.Busy, 16);
        waveformBusyIcon.Classes.Add("se-spin");
        waveformBusyIcon.Margin = new Thickness(0, 0, 5, 0);
        waveformBusyIcon.Bind(Visual.IsVisibleProperty, new Binding(nameof(vm.IsWaveformGenerating)));
        grid.Add(waveformBusyIcon, 0);

        var labelWaveFormText = UiUtil.MakeLabel()
            .WithBindText(vm, vm => vm.WaveformGeneratingText)
            .WithBindVisible(vm, vm => vm.IsWaveformGenerating)
            .WithMarginRight(15);
        labelWaveFormText.Opacity = 0.5;
        grid.Add(labelWaveFormText, 0, 1);

        vm.StatusTextLeftLabel = new TextBlock
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            [!TextBlock.TextProperty] = new Binding(nameof(vm.StatusTextLeft)),
        };
        grid.Add(vm.StatusTextLeftLabel, 0, 2);

        var lockIcon = Glyphs.Make(Glyphs.Lock, 16);
        lockIcon.Margin = new Thickness(4, 0, 8, 0);
        ToolTip.SetTip(lockIcon, Se.Language.General.LockTimeCodes);
        lockIcon.Bind(Visual.IsVisibleProperty, new Binding(nameof(vm.LockTimeCodes)));

        var panelRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                lockIcon,
                IndicatorButton(Glyphs.Filter, Se.Language.General.LayerFilterOn, nameof(vm.ShowLayerFilterIcon), nameof(vm.ShowPickLayerFilterCommand)),
                IndicatorButton(Glyphs.TimerMinus, Se.Language.Main.Menu.SmpteTiming, nameof(vm.IsSmpteTimingEnabled), nameof(vm.ShowSmpteTimingCommand)),
                IndicatorButton(Glyphs.Timer, Se.Language.General.OffsetTimeCodes, nameof(vm.IsVideoOffsetVisible), nameof(vm.ShowVideoSetOffsetCommand)),
                UiUtil.MakeLabel()
                    .WithBindText(vm, vm => vm.VideoOffsetText)
                    .WithBindVisible(vm, vm => vm.IsVideoOffsetVisible).WithMarginRight(15),
                new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 4),
                    [!TextBlock.TextProperty] = new Binding(nameof(vm.StatusTextRight)),
                },
            },
        };
        grid.Add(panelRight, 0, 3);

        return grid;
    }

    /// <summary>A small icon button that shows while a mode is on and opens its settings.</summary>
    private static Button IndicatorButton(string glyph, string tip, string isVisiblePath, string commandPath)
    {
        var button = new Button
        {
            Content = Glyphs.Make(glyph, 16),
            [ToolTip.TipProperty] = tip,
            [!Visual.IsVisibleProperty] = new Binding(isVisiblePath),
            [!Button.CommandProperty] = new Binding(commandPath),
        };
        ChromeStyles.AsTool(button, 24);
        return button;
    }
}
