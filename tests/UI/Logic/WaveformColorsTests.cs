using Avalonia.Media;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;

namespace UITests.Logic;

public class WaveformColorsTests
{
    public static TheoryData<string> Themes => new(ThemePreset.All.Select(t => t.Name));

    [Theory]
    [MemberData(nameof(Themes))]
    public void FromTheme_IsReadableOnItsBackground(string theme)
    {
        var old = Se.Settings.Appearance.Theme;
        try
        {
            Se.Settings.Appearance.Theme = theme;
            var c = WaveformColors.FromPalette(ChromeStyles.MakePalette());

            Assert.True(Contrast(c.Text, c.Background) >= 4.5, $"text {Contrast(c.Text, c.Background):0.0}");
            Assert.True(Contrast(c.Cursor, c.Background) >= 3, $"cursor {Contrast(c.Cursor, c.Background):0.0}");
            Assert.True(Contrast(c.Wave, c.Background) >= 1.8, $"wave {Contrast(c.Wave, c.Background):0.0}");
            Assert.True(Contrast(c.Selected, c.Background) >= 2.5, $"selected wave {Contrast(c.Selected, c.Background):0.0}");
            Assert.True(Contrast(c.Selected, c.Wave) >= 1.5, $"selected vs wave {Contrast(c.Selected, c.Wave):0.0}");
        }
        finally
        {
            Se.Settings.Appearance.Theme = old;
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Palette_TextIsReadable(string theme)
    {
        var old = Se.Settings.Appearance.Theme;
        try
        {
            Se.Settings.Appearance.Theme = theme;
            var p = ChromeStyles.MakePalette();

            Assert.True(Contrast(p.Text, p.Body) >= 7, $"text {Contrast(p.Text, p.Body):0.0}");
            Assert.True(Contrast(p.Text, p.Field) >= 7, $"field text {Contrast(p.Text, p.Field):0.0}");
            Assert.True(Contrast(p.Text, p.Bar) >= 7, $"menu text {Contrast(p.Text, p.Bar):0.0}");
            Assert.True(Contrast(p.Muted, p.Body) >= 3, $"muted {Contrast(p.Muted, p.Body):0.0}");
            Assert.True(Contrast(p.SyntaxTag, p.Field) >= 3, $"tag color {Contrast(p.SyntaxTag, p.Field):0.0}");
        }
        finally
        {
            Se.Settings.Appearance.Theme = old;
        }
    }

    // WCAG contrast ratio
    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
