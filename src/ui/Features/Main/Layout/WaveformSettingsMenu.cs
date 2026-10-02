using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Controls.AudioVisualizerControl;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Features.Main.Layout;

/// <summary>
/// The gear at the bottom right of the waveform: how the waveform looks (style, colors, view) and what each
/// subtitle shows (text, size, #, duration, chars/sec), plus grid lines and the audio track. Every choice is
/// saved and applied at once. The menu is rebuilt each time it opens, so it always shows the current state.
/// </summary>
public static class WaveformSettingsMenu
{
    /// <summary>Wave, selected wave, loud peaks (Dynamic style). Green is SE's default.</summary>
    private static readonly (Func<string> Name, string Wave, string Selected, string High)[] ColorPresets =
    [
        (() => Se.Language.Main.Waveform.ColorGreen, "#FF004600", "#960078FF", "#FFFFA500"),
        (() => Se.Language.Main.Waveform.ColorBlue, "#FF3A68C8", "#B4A8C4FF", "#FFE6EEFF"),
        (() => Se.Language.Main.Waveform.ColorTeal, "#FF1F8576", "#B470E6D6", "#FFE8C050"),
        (() => Se.Language.Main.Waveform.ColorViolet, "#FF6455B8", "#B4C2B8FF", "#FFFF9E65"),
        (() => Se.Language.Main.Waveform.ColorAmber, "#FFA36A12", "#B4FFD58A", "#FFFF5A3C"),
        (() => Se.Language.Main.Waveform.ColorGrey, "#FF5F646B", "#B4D0D4DA", "#FFFFFFFF"),
    ];

    private static readonly (Func<string> Name, string Hex)[] Backgrounds =
    [
        (() => Se.Language.Main.Waveform.BackgroundBlack, "#FF000000"),
        (() => Se.Language.Main.Waveform.BackgroundCharcoal, "#FF16181C"),
        (() => Se.Language.Main.Waveform.BackgroundSlate, "#FF1E242B"),
        (() => Se.Language.Main.Waveform.BackgroundNavy, "#FF0E1626"),
    ];

    private static readonly (Func<string> Name, int Size)[] TextSizes =
    [
        (() => Se.Language.Main.Waveform.TextSizeSmall, 10),
        (() => Se.Language.Main.Waveform.TextSizeMedium, 12),
        (() => Se.Language.Main.Waveform.TextSizeLarge, 14),
    ];

    public static void Fill(MenuFlyout menu, MainViewModel vm)
    {
        var l = Se.Language.Main.Waveform;
        var s = Se.Settings.Waveform;
        var visualizer = vm.AudioVisualizer;
        menu.Items.Clear();
        if (visualizer == null)
        {
            return;
        }

        // How the waveform looks
        var style = InitWaveform.GetWaveformDrawStyle(s.WaveformDrawStyle);
        menu.Items.Add(Sub(l.Style,
            StyleChoice(Se.Language.Waveform.WaveformDrawStyleClassic, WaveformDrawStyle.Classic),
            StyleChoice(Se.Language.Waveform.WaveformDrawStyleFancy, WaveformDrawStyle.Fancy),
            StyleChoice(Se.Language.Waveform.WaveformDrawStyleLines, WaveformDrawStyle.Lines)));

        // Theme: every waveform color follows the UI theme; any other choice switches to own colors
        var useTheme = s.UseThemeColors;
        var colors = new List<Control>
        {
            Choice(Swatch(l.ColorTheme, WaveformColors.FromTheme().Wave.FromColorToHex()), useTheme, () =>
            {
                s.UseThemeColors = true;
                WaveformColors.ApplyTo(visualizer);
            }),
            new Separator(),
            Label(l.Waveform),
        };
        colors.AddRange(ColorPresets.Select(preset => Choice(Swatch(preset.Name(), preset.Wave), !useTheme && Same(s.WaveformColor, preset.Wave),
            () => SetWave(preset.Wave, preset.Selected, preset.High))));
        colors.Add(new Separator());
        colors.Add(Label(l.Background));
        colors.AddRange(Backgrounds.Select(back => Choice(Swatch(back.Name(), back.Hex), !useTheme && Same(s.WaveformBackgroundColor, back.Hex),
            () => SetBackground(back.Hex))));
        colors.Add(new Separator());
        colors.Add(Check(l.ColorSubtitles, s.WaveformColorSubtitles, on => s.WaveformColorSubtitles = on));
        colors.Add(new Separator());
        var current = WaveformColors.Current;
        colors.Add(Custom(l.CustomWaveformColor, current.Wave.FromColorToHex(), !useTheme && !ColorPresets.Any(p => Same(s.WaveformColor, p.Wave)), color =>
            SetWave(color.FromColorToHex(), Color.FromArgb(0xB4, Lighter(color.R), Lighter(color.G), Lighter(color.B)).FromColorToHex(), current.FancyHigh.FromColorToHex())));
        colors.Add(Custom(l.CustomBackgroundColor, current.Background.FromColorToHex(), !useTheme && !Backgrounds.Any(b => Same(s.WaveformBackgroundColor, b.Hex)), color =>
            SetBackground(color.FromColorToHex())));
        menu.Items.Add(Sub(l.Colors, colors.ToArray()));

        if (vm.ShowWaveformDisplayModeSeparator) // only when a spectrogram exists
        {
            var mode = visualizer.GetDisplayMode();
            menu.Items.Add(Sub(l.View,
                Choice(l.ViewWaveform, mode == WaveformDisplayMode.OnlyWaveform, () => vm.WaveformShowOnlyWaveformCommand.Execute(null)),
                Choice(l.ViewSpectrogram, mode == WaveformDisplayMode.OnlySpectrogram, () => vm.WaveformShowOnlySpectrogramCommand.Execute(null)),
                Choice(l.ViewBoth, mode == WaveformDisplayMode.WaveformAndSpectrogram, () => vm.WaveformShowWaveformAndSpectrogramCommand.Execute(null))));
        }

        // What each subtitle shows
        menu.Items.Add(new Separator());
        menu.Items.Add(Check(l.SubtitleText, s.WaveformShowText, on => s.WaveformShowText = on));
        menu.Items.Add(Sub(l.TextSize, TextSizes.Select(size => Choice(size.Name(), s.WaveformTextFontSize == size.Size,
            () => s.WaveformTextFontSize = size.Size)).ToArray()));
        menu.Items.Add(Check(l.LineNumber, s.WaveformShowNumber, on => s.WaveformShowNumber = on));
        menu.Items.Add(Check(l.Duration, s.WaveformShowDuration, on => s.WaveformShowDuration = on));
        menu.Items.Add(Check(l.CharsPerSecond, s.WaveformShowCps, on => s.WaveformShowCps = on));

        // Timeline and audio
        menu.Items.Add(new Separator());
        menu.Items.Add(Check(l.GridLines, s.DrawGridLines, on => visualizer.DrawGridLines = s.DrawGridLines = on));
        menu.Items.Add(AudioTracks(vm));

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = l.MoreWaveformSettings, Command = vm.CommandShowSettingsCommand });
        return;

        MenuItem StyleChoice(string name, WaveformDrawStyle value) => Choice(name, style == value, () =>
        {
            s.WaveformDrawStyle = value.ToString();
            visualizer.WaveformDrawStyle = value;
        });

        // leaving Theme keeps its other colors (e.g. a new background keeps the theme's wave and text)
        void SetWave(string wave, string selected, string high)
        {
            WaveformColors.UseOwnColors();
            s.WaveformColor = wave;
            s.WaveformSelectedColor = selected;
            s.WaveformFancyHighColor = high;
            WaveformColors.ApplyTo(visualizer);
        }

        void SetBackground(string hex)
        {
            WaveformColors.UseOwnColors();
            s.WaveformBackgroundColor = hex;
            WaveformColors.ApplyTo(visualizer);
        }

        // "Custom..." opens the color picker on the current color; checked (with its swatch) when no preset matches
        MenuItem Custom(string header, string currentHex, bool isCurrent, Action<Color> apply) => new()
        {
            Header = isCurrent ? Swatch(header, currentHex) : header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isCurrent,
            Command = new AsyncRelayCommand(async () =>
            {
                if (await vm.PickColorAsync(currentHex.FromHexToColor()) is { } color)
                {
                    Commit(() => apply(color));
                }
            }),
        };

        // Every change: save, refresh the waveform's cached pens/fonts, redraw
        MenuItem Choice(object header, bool isChecked, Action apply) => new()
        {
            Header = header,
            ToggleType = MenuItemToggleType.Radio,
            IsChecked = isChecked,
            Command = new RelayCommand(() => Commit(apply)),
        };

        MenuItem Check(string header, bool isChecked, Action<bool> apply) => new()
        {
            Header = header,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = isChecked,
            Command = new RelayCommand(() => Commit(() => apply(!isChecked))),
        };

        void Commit(Action apply)
        {
            apply();
            visualizer.ResetCache();
            visualizer.InvalidateVisual();
            Se.SaveSettings();
        }
    }

    /// <summary>The same tracks (and check mark) as Video → Audio tracks, which SE keeps up to date.</summary>
    private static MenuItem AudioTracks(MainViewModel vm)
    {
        var tracks = vm.AudioTraksMenuItem.Items.OfType<MenuItem>().ToList();
        var item = new MenuItem { Header = Se.Language.Main.Waveform.AudioTrack, IsEnabled = tracks.Count > 0 };
        foreach (var track in tracks)
        {
            item.Items.Add(new MenuItem
            {
                Header = track.Header,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = track.Icon != null, // the Video menu marks the current track with a check icon
                Command = track.Command,
                CommandParameter = track.CommandParameter,
            });
        }

        if (tracks.Count == 0)
        {
            ToolTip.SetTip(item, Se.Language.Main.Waveform.NoAudioTracks);
        }

        return item;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static byte Lighter(byte v) => (byte)(v + (255 - v) * 0.55); // selected wave: the custom color, lighter

    /// <summary>A section title inside a submenu (not clickable).</summary>
    private static MenuItem Label(string text) => new() { Header = text, IsEnabled = false };

    private static MenuItem Sub(string header, params Control[] items)
    {
        var item = new MenuItem { Header = header };
        foreach (var child in items)
        {
            item.Items.Add(child);
        }

        return item;
    }

    private static StackPanel Swatch(string name, string hex) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        Children =
        {
            new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(hex.FromHexToColor()),
                VerticalAlignment = VerticalAlignment.Center,
            },
            new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
        },
    };
}
