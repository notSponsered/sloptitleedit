using Avalonia.Media;
using Nikse.SubtitleEdit.Controls.AudioVisualizerControl;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.Logic.Config;
using System;

namespace Nikse.SubtitleEdit.Logic;

/// <summary>
/// The waveform's colors: from the UI theme (waveform settings menu → Colors → Theme, the default), or the
/// user's own (a color preset, a custom color or Settings). Theme colors follow theme changes as they happen.
/// </summary>
public static class WaveformColors
{
    public sealed record Set(Color Background, Color Wave, Color Selected, Color FancyHigh, Color Cursor,
        Color ParagraphBackground, Color ParagraphSelectedBackground, Color Text);

    public static Set Current => Se.Settings.Waveform.UseThemeColors ? FromTheme() : FromSettings();

    public static Set FromSettings()
    {
        var s = Se.Settings.Waveform;
        return new Set(s.WaveformBackgroundColor.FromHexToColor(), s.WaveformColor.FromHexToColor(), s.WaveformSelectedColor.FromHexToColor(),
            s.WaveformFancyHighColor.FromHexToColor(), s.WaveformCursorColor.FromHexToColor(), s.ParagraphBackground.FromHexToColor(),
            s.ParagraphSelectedBackground.FromHexToColor(), s.WaveformTextColor.FromHexToColor());
    }

    /// <summary>Derived from the panel colors: the wave in the theme's accent, text and playhead in its text color.</summary>
    public static Set FromTheme() => FromPalette(ChromeStyles.Current);

    internal static Set FromPalette(ChromeStyles.Palette p)
    {
        var dark = p.Body.R * 0.299 + p.Body.G * 0.587 + p.Body.B * 0.114 < 128;
        var background = dark ? p.Ground : p.Body;
        return new Set(
            Background: background,
            Wave: dark ? Mix(p.Accent, Colors.White, 0.1) : Mix(p.Accent, background, 0.2),
            Selected: dark ? Mix(p.Accent, Colors.White, 0.6) : Mix(p.Accent, Colors.Black, 0.45),
            FancyHigh: Color.Parse(dark ? "#ffb07a" : "#d9622b"),
            Cursor: p.Text,
            ParagraphBackground: WithAlpha(p.Text, (byte)(dark ? 0x26 : 0x1a)),
            ParagraphSelectedBackground: WithAlpha(p.Accent, 0x55),
            Text: p.Text);
    }

    public static void ApplyTo(AudioVisualizer visualizer)
    {
        var c = Current;
        var s = Se.Settings.Waveform;
        visualizer.WaveformBackgroundColor = c.Background;
        visualizer.WaveformColor = c.Wave;
        visualizer.WaveformSelectedColor = c.Selected;
        visualizer.WaveformFancyHighColor = c.FancyHigh;
        visualizer.WaveformCursorColor = c.Cursor;
        visualizer.ParagraphBackground = c.ParagraphBackground;
        visualizer.ParagraphSelectedBackground = c.ParagraphSelectedBackground;
        visualizer.WaveformTextColor = c.Text;
        visualizer.WaveformShotChangeColor = s.WaveformShotChangeColor.FromHexToColor();
        visualizer.WaveformParagraphLeftColor = s.WaveformParagraphLeftColor.FromHexToColor();
        visualizer.WaveformParagraphRightColor = s.WaveformParagraphRightColor.FromHexToColor();
        visualizer.ResetCache();
        visualizer.InvalidateVisual();
    }

    /// <summary>
    /// Leaves theme colors: the theme's current colors become the user's own, so picking one color (e.g. only
    /// a background) changes just that one.
    /// </summary>
    public static void UseOwnColors()
    {
        var s = Se.Settings.Waveform;
        if (!s.UseThemeColors)
        {
            return;
        }

        Save(FromTheme());
        s.UseThemeColors = false;
    }

    public static void Save(Set c)
    {
        var s = Se.Settings.Waveform;
        s.WaveformBackgroundColor = c.Background.FromColorToHex();
        s.WaveformColor = c.Wave.FromColorToHex();
        s.WaveformSelectedColor = c.Selected.FromColorToHex();
        s.WaveformFancyHighColor = c.FancyHigh.FromColorToHex();
        s.WaveformCursorColor = c.Cursor.FromColorToHex();
        s.ParagraphBackground = c.ParagraphBackground.FromColorToHex();
        s.ParagraphSelectedBackground = c.ParagraphSelectedBackground.FromColorToHex();
        s.WaveformTextColor = c.Text.FromColorToHex();
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t)
    {
        byte F(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
        return Color.FromRgb(F(a.R, b.R), F(a.G, b.G), F(a.B, b.B));
    }
}
