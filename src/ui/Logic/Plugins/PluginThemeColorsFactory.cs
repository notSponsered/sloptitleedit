using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Logic.Plugins;

/// <summary>Builds <see cref="PluginThemeColors"/> from the active color theme (<see cref="ChromeStyles.Current"/>).</summary>
internal static class PluginThemeColorsFactory
{
    public static PluginThemeColors Build()
    {
        var p = ChromeStyles.Current;
        return new PluginThemeColors
        {
            IsDark = p.Dark,
            BackgroundColor = p.Body.FromColorToHex(),
            ForegroundColor = p.Text.FromColorToHex(),
            AccentColor = p.Accent.FromColorToHex(),
            BackgroundColorLighter = p.TabHover.FromColorToHex(),
            BackgroundColorHeader = p.Header.FromColorToHex(),
            BookmarkColor = Se.Settings.Appearance.BookmarkColor,
        };
    }
}
