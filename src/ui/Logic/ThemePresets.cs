using Avalonia;
using Avalonia.Platform;
using Nikse.SubtitleEdit.Logic.Config;
using System.Linq;

namespace Nikse.SubtitleEdit.Logic;

/// <summary>
/// A color theme: a few base colors. Every other UI color (bars, dividers, hover, selection, fields, the
/// waveform) is derived from them in <see cref="Features.Main.Layout.ChromeStyles.MakePalette"/>, and the
/// user's own colors (Settings → Appearance) are laid over the theme's.
/// </summary>
/// <param name="Background">Panels and windows; dark or light decides the rest.</param>
/// <param name="Accent">Selection, active tab, toggles that are on; "" = the system accent color.</param>
/// <param name="Bar">The top bar (menu, workspaces); null = derived from the background.</param>
/// <param name="Divider">The lines and gaps between areas; null = derived from the background.</param>
/// <param name="Cards">Areas as rounded cards on gaps (Blender) instead of flush panels with 1 px dividers.</param>
public sealed record ThemePreset(string Name, string Background, string Text, string Accent, string? Bar = null, string? Divider = null, bool Cards = false)
{
    public const string System = "System";

    public static readonly ThemePreset[] All =
    [
        new("Light", "#f9fafb", "#1d1f23", ""),
        new("Dark", "#242528", "#dcdcdc", ""),
        new("Classic", "#f4f2e8", "#000000", "#316ac5", Bar: "#ece9d8", Divider: "#aca899"),
        new("Pastel", "#f8f5ff", "#2e2640", "#a27bd1", Bar: "#f0ebff", Divider: "#d9cde6"),
        new("Blender", "#282828", "#e6e6e6", "#4772b3", Bar: "#1d1d1d", Divider: "#161616", Cards: true),

        // one color each
        new("Slate", "#262a33", "#d8dde6", "#5b8def"),
        new("Ocean", "#15213a", "#d6e2f5", "#3fa7f5"),
        new("Forest", "#1b2620", "#d5e5d9", "#4fbf7c"),
        new("Plum", "#261d2e", "#e6dbef", "#b57edc"),
        new("Ember", "#2b211c", "#efe0d6", "#f08a4b"),
        new("Paper", "#fbf8f1", "#2b2621", "#2f5fa8", Bar: "#f1ece0", Divider: "#d8d0bf"),
        new("Sky", "#f3f8fd", "#1b2a3a", "#2f80d1"),

        // combinations (after well-known editor palettes)
        new("Arctic", "#2e3440", "#e5e9f0", "#88c0d0", Bar: "#272c36", Divider: "#21252e"),
        new("Dusk", "#1e1e2e", "#cdd6f4", "#cba6f7", Bar: "#181825", Divider: "#11111b"),
        new("Solar dark", "#002b36", "#eee8d5", "#268bd2", Bar: "#00242d", Divider: "#001c24"),
        new("Solar light", "#fdf6e3", "#073642", "#268bd2", Bar: "#eee8d5", Divider: "#d6cfb9"),
        new("Retro", "#282828", "#ebdbb2", "#fabd2f", Bar: "#1d2021", Divider: "#151718"),
        new("Rosé", "#232136", "#e0def4", "#ea9a97", Bar: "#1c1a2c", Divider: "#151320"),
        new("High contrast", "#000000", "#ffffff", "#ffd400", Bar: "#000000", Divider: "#8a8a8a"),
    ];

    /// <summary>The chosen theme; "System" follows the operating system's dark/light setting.</summary>
    public static ThemePreset Current => Find(Se.Settings.Appearance.Theme);

    public static ThemePreset Find(string name)
    {
        if (name == System)
        {
            var os = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant;
            name = os == PlatformThemeVariant.Dark ? "Dark" : "Light";
        }

        return All.FirstOrDefault(p => p.Name == name) ?? All[0];
    }

    /// <summary>Theme names as shown: the classic four translated, the rest as named.</summary>
    public static string Display(string name) => name switch
    {
        System => Se.Language.General.System,
        "Light" => Se.Language.General.Light,
        "Dark" => Se.Language.General.Dark,
        "Classic" => Se.Language.General.Classic,
        _ => name,
    };
}
